using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Globalization;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportInsertSelectNullability(InsertStatement node, IReadOnlyCollection<TableColumn> knownColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0)
            {
                return;
            }

            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            var columnsByName = knownColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            var tables = TablesFor(query, NestedQueries(query));
            foreach (var pair in node.InsertSpecification.Columns.Zip(query.SelectElements))
            {
                if (!TryGetNonNullableTarget(pair.First, pair.Second, columnsByName, out var targetName, out var targetColumn, out var scalar))
                {
                    continue;
                }

                if (scalar.Expression is NullLiteral nullLiteral)
                {
                    Add("nullability-violation", nullLiteral, $"NULL is assigned to non-nullable column '{targetName}'.", "warning");
                    continue;
                }

                if (IsNullableSourceInsert(scalar.Expression, tables, query.WhereClause?.SearchCondition, out var sourceMetadata))
                {
                    Add("nullable-source-insert", scalar.Expression, $"Nullable source column '{sourceMetadata.ColumnName}' is inserted into non-nullable column '{targetName}'.", "info");
                }
            }
        }

        private static bool TryGetNonNullableTarget(
            ColumnReferenceExpression target,
            TSqlFragment source,
            IReadOnlyDictionary<string, TableColumn> columnsByName,
            out string targetName,
            out TableColumn targetColumn,
            out SelectScalarExpression scalar)
        {
            targetName = "";
            targetColumn = null!;
            scalar = null!;

            var name = target.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
            if (name is null)
            {
                return false;
            }

            if (!columnsByName.TryGetValue(name, out var column))
            {
                return false;
            }

            if (column.IsNullable != false)
            {
                return false;
            }

            if (source is not SelectScalarExpression scalarExpression)
            {
                return false;
            }

            targetName = name;
            targetColumn = column;
            scalar = scalarExpression;

            return true;
        }

        private bool IsNullableSourceInsert(
            ScalarExpression expression,
            IReadOnlyList<TableReference> tables,
            BooleanExpression? where,
            out TableColumn sourceMetadata)
        {
            sourceMetadata = null!;

            if (expression is not ColumnReferenceExpression sourceColumn)
            {
                return false;
            }

            if (!TryGetColumnMetadata(sourceColumn, tables, out sourceMetadata))
            {
                return false;
            }

            if (sourceMetadata.IsNullable != true)
            {
                return false;
            }

            return !IsGuaranteedNotNull(where, sourceColumn);
        }

        /// <summary>Checks whether a WHERE expression guarantees that a source column is not NULL.</summary>
        private static bool IsGuaranteedNotNull(BooleanExpression? expression, ColumnReferenceExpression column)
        {
            if (expression is null)
            {
                return false;
            }

            if (expression is BooleanIsNullExpression isNull)
            {
                return isNull.IsNot
                    && isNull.Expression is ColumnReferenceExpression reference
                    && string.Equals(ColumnReferenceText(reference), ColumnReferenceText(column), StringComparison.OrdinalIgnoreCase);
            }

            if (expression is BooleanParenthesisExpression parenthesis)
            {
                return IsGuaranteedNotNull(parenthesis.Expression, column);
            }

            if (expression is not BooleanBinaryExpression binary)
            {
                return false;
            }

            var first = IsGuaranteedNotNull(binary.FirstExpression, column);
            var second = IsGuaranteedNotNull(binary.SecondExpression, column);

            return binary.BinaryExpressionType == BooleanBinaryExpressionType.And
                ? first || second
                : first && second;
        }

        private static string ColumnReferenceText(ColumnReferenceExpression column) => string.Join('.',
            column.MultiPartIdentifier?.Identifiers.Select(identifier => identifier.Value) ?? []);

        private void ReportInsertExecuteCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not ExecuteInsertSource { Execute: var execute })
            {
                return;
            }

            if (execute.ExecutableEntity is not ExecutableProcedureReference procedure)
            {
                return;
            }

            if (procedure.ProcedureReference?.ProcedureReference?.Name is not { } name)
            {
                return;
            }

            var objectName = ObjectNames.Format(name, ObjectNames.StandardDefaultSchema);
            if (!resultColumnsByObject.TryGetValue(objectName, out var resultColumns))
            {
                return;
            }

            if (!TryInsertTargetColumnCount(node.InsertSpecification, out var targetColumnCount))
            {
                return;
            }

            if (targetColumnCount != resultColumns.Length)
            {
                Add("insert-exec-count", node, $"INSERT targets {targetColumnCount} column(s), but procedure '{objectName}' returns {resultColumns.Length}.", "warning");
            }

            ReportInsertExecuteTypes(node, resultColumns);
        }

        /// <summary>Reports result-set type-family differences only when both sides are explicit.</summary>
        private void ReportInsertExecuteTypes(InsertStatement node, IReadOnlyList<ResultColumn> resultColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0)
            {
                return;
            }

            if (node.InsertSpecification.Target is not NamedTableReference target)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out _))
            {
                return;
            }

            if (!metadataByTable.TryGetValue(tableName, out var targetColumns))
            {
                return;
            }

            var targetByName = targetColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in node.InsertSpecification.Columns.Zip(resultColumns))
            {
                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null)
                {
                    continue;
                }

                if (!targetByName.TryGetValue(targetName, out var targetColumn))
                {
                    continue;
                }

                if (pair.Second.DataType is not { } resultType)
                {
                    continue;
                }

                if (!TypeFamiliesDiffer(targetColumn.DataType, resultType))
                {
                    continue;
                }

                Add("insert-exec-type", pair.First, $"Procedure result column '{pair.Second.ColumnName}' has type '{resultType}', but target column '{targetName}' has type '{targetColumn.DataType}'.", "info");
            }
        }

        private static bool TypeFamiliesDiffer(string first, string second)
        {
            var firstFamily = TypeFamily(first);
            var secondFamily = TypeFamily(second);

            return firstFamily != "other"
                && secondFamily != "other"
                && !string.Equals(firstFamily, secondFamily, StringComparison.Ordinal);
        }

        private bool TryInsertTargetColumnCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference namedTarget
                && TryTableColumns(namedTarget, out _, out var knownColumns))
            {
                count = knownColumns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference localTarget)
            {
                count = LocalColumnsFor(localTarget.SchemaObject.BaseIdentifier.Value, LineOf(localTarget)).Count;

                if (count > 0)
                {
                    return true;
                }
            }

            if (specification.Target is VariableTableReference variableTarget)
            {
                count = LocalColumnsFor(variableTarget.Variable.Name, LineOf(variableTarget)).Count;

                return count > 0;
            }

            count = 0;

            return false;
        }

        private void ReportMissingRequiredInsertColumns(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            if (node.InsertSpecification.Columns.Count == 0
                && node.InsertSpecification.InsertSource is not ValuesInsertSource { IsDefaultValues: true })
            {
                return;
            }

            var inserted = node.InsertSpecification.Columns
                .Select(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns.Where(IsRequiredInsertColumn).Where(column => !inserted.Contains(column.ColumnName)))
            {
                Add("missing-required-insert-column", node.InsertSpecification.Target, $"INSERT omits required column '{column.ColumnName}' from '{tableName}'.", "warning");
            }
        }

        private static bool IsRequiredInsertColumn(TableColumn column) =>
            column.IsNullable == false
            && !column.IsIdentity
            && !column.IsComputed
            && column.DefaultValue is null;

        private void ReportIdentityInsert(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            var identityColumns = columns
                .Where(column => column.IsIdentity)
                .Select(column => column.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } identifier
                    && identityColumns.Contains(identifier.Value)))
            {
                Add("identity-insert", column, $"INSERT explicitly targets identity column '{column.MultiPartIdentifier!.Identifiers[^1].Value}'; verify that IDENTITY_INSERT is enabled.", "warning");
            }
        }

        private void ReportComputedInsert(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            var computedColumns = columns
                .Where(column => column.IsComputed)
                .Select(column => column.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } identifier
                    && computedColumns.Contains(identifier.Value)))
            {
                Add("computed-insert", column, $"INSERT explicitly targets computed column '{column.MultiPartIdentifier!.Identifiers[^1].Value}'; computed columns cannot be assigned.");
            }
        }

        private void ValidateInsertColumns(
            InsertStatement node,
            string tableName,
            IReadOnlySet<string> knownColumns
        )
        {
            foreach (var column in node.InsertSpecification.Columns)
            {
                var columnName = column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;

                if (columnName is not null && !knownColumns.Contains(columnName))
                {
                    Add("unknown-insert-column", column, InvalidColumnMessage("INSERT column", columnName, tableName, knownColumns));
                }
            }
        }

        private void ReportDuplicateInsertColumns(InsertStatement node)
        {
            foreach (var duplicates in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.Count > 0)
                .GroupBy(column => column.MultiPartIdentifier!.Identifiers[^1].Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var duplicate in duplicates.Skip(1))
                {
                    Add("duplicate-insert-column", duplicate, $"Column '{duplicates.Key}' appears more than once in this INSERT list.");
                }
            }
        }

        private void ReportInsertValues(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not ValuesInsertSource values)
            {
                return;
            }

            var targetCount = node.InsertSpecification.Columns.Count;
            var targetDescription = $"{targetCount} target columns";
            if (targetCount == 0)
            {
                if (values.IsDefaultValues)
                {
                    return;
                }

                if (node.InsertSpecification.Target is not NamedTableReference target)
                {
                    return;
                }

                if (!TryTableColumns(target, out var tableName, out _))
                {
                    return;
                }

                if (!metadataByTable.TryGetValue(tableName, out var columns))
                {
                    return;
                }

                if (columns.Any(column => column.IsIdentity || column.IsComputed))
                {
                    return;
                }

                targetCount = columns.Length;
                targetDescription = $"{targetCount} table columns";
            }

            foreach (var row in values.RowValues.Where(row => row.ColumnValues.Count != targetCount))
            {
                Add("insert-value-count", row, $"INSERT targets {targetDescription} but supplies {row.ColumnValues.Count} values.");
            }
        }

        private void ReportInsertLiteralTypes(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns)
                || node.InsertSpecification.InsertSource is not ValuesInsertSource values)
            {
                return;
            }

            var metadata = columns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            foreach (var row in values.RowValues)
            {
                foreach (var pair in node.InsertSpecification.Columns.Zip(row.ColumnValues))
                {
                    ReportInsertLiteralType(pair.First, pair.Second, metadata);
                }
            }
        }

        private void ReportInsertLiteralType(
            ColumnReferenceExpression target,
            ScalarExpression value,
            IReadOnlyDictionary<string, TableColumn> metadata)
        {
            var name = target.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;

            if (name is null || !metadata.TryGetValue(name, out var column))
            {
                return;
            }

            ReportStringLiteralConversion(value, name, column);
            ReportNullabilityViolation(value, name, column);
            ReportNumericLiteralPrecision(value, name, column);
        }

        private void ReportStringLiteralConversion(ScalarExpression value, string name, TableColumn column)
        {
            if (value is not StringLiteral literal || !IsStringConversionTarget(column.DataType))
            {
                return;
            }

            Add("implicit-conversion", value, $"String literal '{literal.Value}' is assigned to '{column.DataType}' column '{name}' and may require an implicit conversion.", "warning");

            if (HasDateTimePrecisionLoss(literal.Value, column))
            {
                Add("precision-loss", value, $"Date/time literal may lose precision when assigned to '{column.DataType}' column '{name}'.", "warning");
            }
        }

        private void ReportNullabilityViolation(ScalarExpression value, string name, TableColumn column)
        {
            if (value is not NullLiteral || column.IsNullable != false)
            {
                return;
            }

            Add("nullability-violation", value, $"NULL is assigned to non-nullable column '{name}'.", "warning");
        }

        private void ReportNumericLiteralPrecision(ScalarExpression value, string name, TableColumn column)
        {
            if (value is not (NumericLiteral or MoneyLiteral) || !HasPrecisionLoss(value, column))
            {
                return;
            }

            Add("precision-loss", value, $"Numeric literal may lose precision when assigned to '{column.DataType}' column '{name}'.", "warning");
        }

        private static bool HasPrecisionLoss(ScalarExpression expression, TableColumn column)
        {
            var value = expression switch
            {
                NumericLiteral numeric => numeric.Value,
                MoneyLiteral money => money.Value,
                _ => "",
            };

            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (IsIntegerType(column.DataType))
            {
                return number != decimal.Truncate(number);
            }

            if (IsMoneyType(column.DataType))
            {
                return DecimalScale(number) > 4;
            }

            if (IsFloatType(column.DataType))
            {
                var maximumDigits = column.DataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase) ? 7 : 15;
                return SignificantDigits(value) > maximumDigits;
            }

            return column.Scale is { } scale && DecimalScale(number) > scale;
        }

        private static bool HasDateTimePrecisionLoss(string value, TableColumn column)
        {
            var type = column.DataType;

            if (type.Equals("DATE", StringComparison.OrdinalIgnoreCase))
            {
                return value.Contains('T') || value.Contains(' ');
            }

            if (type.Equals("SMALLDATETIME", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && (date.Second != 0 || date.Millisecond != 0);
            }

            if (!IsDateTimeType(type))
            {
                return false;
            }

            var decimalSeparator = value.IndexOf('.');
            if (decimalSeparator < 0)
            {
                return false;
            }

            var fraction = value[(decimalSeparator + 1)..];
            var fractionLength = fraction.TakeWhile(char.IsDigit).Count();
            var allowedScale = column.Scale ?? (type.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("DATETIME2", StringComparison.OrdinalIgnoreCase) ? 3 : 7);

            return fractionLength > allowedScale;
        }

        private static bool IsDateTimeType(string dataType) => dataType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DATETIMEOFFSET", StringComparison.OrdinalIgnoreCase);

        private static int SignificantDigits(string value)
        {
            var digits = value.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            return digits.TrimEnd('0').Length;
        }

        private static int DecimalScale(decimal value)
        {
            value = decimal.Abs(value);
            var bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0x7F;
        }

        private static bool IsIntegerType(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase);

        private static bool IsMoneyType(string dataType) => dataType.StartsWith("MONEY", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLMONEY", StringComparison.OrdinalIgnoreCase);

        private static bool IsFloatType(string dataType) => dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);

        private static bool IsStringConversionTarget(string dataType) => IsNumericType(dataType)
            || dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase);

        private static bool IsNumericType(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);

        private void ReportInsertSelectCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            if (!TrySelectColumnCount(query, out var selectedColumnCount))
            {
                return;
            }

            if (!TryInsertTargetCount(node.InsertSpecification, out var targetColumnCount))
            {
                return;
            }

            if (targetColumnCount == selectedColumnCount)
            {
                return;
            }

            var targetDescription = node.InsertSpecification.Columns.Count > 0
                ? $"lists {targetColumnCount} target columns"
                : $"targets {targetColumnCount} columns";

            Add("insert-select-count", query, $"INSERT {targetDescription} but SELECT returns {selectedColumnCount} expressions.");
        }

        private bool TryInsertTargetCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference target
                && TryTableColumns(target, out _, out var columns))
            {
                count = columns.Count;

                return true;
            }

            count = 0;

            return false;
        }

        private bool TrySelectColumnCount(QuerySpecification query, out int count)
        {
            var tables = TablesFor(query, NestedQueries(query));
            count = 0;

            foreach (var element in query.SelectElements)
            {
                if (element is SelectScalarExpression)
                {
                    count++;
                    continue;
                }

                if (element is not SelectStarExpression star)
                {
                    count = 0;

                    return false;
                }

                var qualifier = star.Qualifier?.Identifiers.LastOrDefault()?.Value;
                var sources = qualifier is null ? tables : tables.Where(table => table.Matches(qualifier));
                var sourceList = sources.ToArray();

                if (sourceList.Length == 0)
                {
                    count = 0;

                    return false;
                }

                count += sourceList.Sum(table => table.Columns.Count);
            }

            return true;
        }


    }
}
