using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Globalization;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportMissingRequiredInsertColumns(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            if (HasNoExplicitColumnsAndIsNotDefaultValues(node.InsertSpecification))
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

        private static bool HasNoExplicitColumnsAndIsNotDefaultValues(InsertSpecification specification)
        {
            if (specification.Columns.Count != 0)
            {
                return false;
            }

            return specification.InsertSource is not ValuesInsertSource { IsDefaultValues: true };
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
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            if (node.InsertSpecification.InsertSource is not ValuesInsertSource values)
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
    }
}
