using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(BooleanComparisonExpression node)
        {
            ReportImplicitFilterConversion(node);
            ReportForeignKeyJoinMismatch(node);
            ReportJoinTypeMismatch(node);
            base.Visit(node);
        }

        private bool TryGetTypeFamilyMismatch(
            string targetColumnName,
            ColumnReferenceExpression sourceColumn,
            IReadOnlyDictionary<string, TableColumn> targetColumnsByName,
            IReadOnlyList<TableReference> tables,
            out TableColumn targetColumn,
            out TableColumn sourceMetadata)
        {
            targetColumn = null!;
            sourceMetadata = null!;

            if (!targetColumnsByName.TryGetValue(targetColumnName, out var candidate)
                || !TryGetColumnMetadata(sourceColumn, tables, out var source))
            {
                return false;
            }

            targetColumn = candidate;
            sourceMetadata = source;

            return TypeFamiliesDiffer(targetColumn.DataType, sourceMetadata.DataType);
        }

        private bool TryGetCaseTypeFamily(
            CaseExpression expression,
            IReadOnlyList<TableReference> tables,
            out string family)
        {
            family = "";
            if (expression.ElseExpression is null)
            {
                return false;
            }

            IEnumerable<ScalarExpression> branchExpressions = expression switch
            {
                SearchedCaseExpression searched => searched.WhenClauses.Select(clause => clause.ThenExpression),
                SimpleCaseExpression simple => simple.WhenClauses.Select(clause => clause.ThenExpression),
                _ => [],
            };
            var branchFamilies = branchExpressions
                .Append(expression.ElseExpression)
                .Select(branch => TryGetExpressionTypeFamily(branch, tables, out var branchFamily)
                    ? branchFamily
                    : null)
                .ToArray();
            if (branchFamilies.Length == 0 || branchFamilies.Any(familyName => familyName is null))
            {
                return false;
            }

            var distinctFamilies = branchFamilies
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctFamilies.Length != 1)
            {
                return false;
            }

            family = distinctFamilies[0];

            return true;
        }

        private bool TryGetExpressionTypeFamily(
            ScalarExpression expression,
            IReadOnlyList<TableReference> tables,
            out string family)
        {
            if (expression is CaseExpression nestedCase)
            {
                return TryGetCaseTypeFamily(nestedCase, tables, out family);
            }

            if (expression is ColumnReferenceExpression column
                && TryGetColumnMetadata(column, tables, out var metadata))
            {
                family = TypeFamily(metadata.DataType);

                return true;
            }

            if (expression is CastCall castCall)
            {
                return TryGetDataTypeFamily(castCall.DataType, out family);
            }

            if (expression is TryCastCall tryCastCall)
            {
                return TryGetDataTypeFamily(tryCastCall.DataType, out family);
            }

            if (expression is BinaryExpression binary
                && IsNumericOperation(binary.BinaryExpressionType)
                && TryGetExpressionTypeFamily(binary.FirstExpression, tables, out var firstFamily)
                && TryGetExpressionTypeFamily(binary.SecondExpression, tables, out var secondFamily)
                && firstFamily == "numeric"
                && secondFamily == "numeric")
            {
                family = "numeric";

                return true;
            }

            if (expression is NumericLiteral or IntegerLiteral or RealLiteral or MoneyLiteral)
            {
                family = "numeric";

                return true;
            }

            if (expression is StringLiteral)
            {
                family = "string";

                return true;
            }

            family = "";

            return false;
        }

        private bool TryGetComputedTypeFamily(
            ScalarExpression expression,
            IReadOnlyList<TableReference> tables,
            out string family)
        {
            if (expression is not CaseExpression
                and not CastCall
                and not TryCastCall
                and not BinaryExpression
                and not NumericLiteral
                and not IntegerLiteral
                and not RealLiteral
                and not MoneyLiteral)
            {
                family = "";

                return false;
            }

            return TryGetExpressionTypeFamily(expression, tables, out family);
        }

        private static string ComputedExpressionName(ScalarExpression expression) => expression switch
        {
            CaseExpression => "CASE",
            CastCall or TryCastCall => "CAST",
            BinaryExpression => "arithmetic",
            NumericLiteral or IntegerLiteral or RealLiteral or MoneyLiteral => "numeric literal",
            _ => "computed",
        };

        private static bool TryGetDataTypeFamily(DataTypeReference dataType, out string family)
        {
            if (dataType is not SqlDataTypeReference sqlType)
            {
                family = "";

                return false;
            }

            family = sqlType.SqlDataTypeOption switch
            {
                SqlDataTypeOption.BigInt
                or SqlDataTypeOption.Int
                or SqlDataTypeOption.SmallInt
                or SqlDataTypeOption.TinyInt
                or SqlDataTypeOption.Bit
                or SqlDataTypeOption.Decimal
                or SqlDataTypeOption.Numeric
                or SqlDataTypeOption.Money
                or SqlDataTypeOption.SmallMoney
                or SqlDataTypeOption.Float
                or SqlDataTypeOption.Real => "numeric",
                SqlDataTypeOption.Date
                or SqlDataTypeOption.Time
                or SqlDataTypeOption.DateTime
                or SqlDataTypeOption.SmallDateTime
                or SqlDataTypeOption.DateTime2
                or SqlDataTypeOption.DateTimeOffset => "datetime",
                SqlDataTypeOption.Char
                or SqlDataTypeOption.VarChar
                or SqlDataTypeOption.Text
                or SqlDataTypeOption.NChar
                or SqlDataTypeOption.NVarChar
                or SqlDataTypeOption.NText => "string",
                SqlDataTypeOption.UniqueIdentifier => "guid",
                _ => "",
            };

            return family.Length > 0;
        }

        private static bool IsNumericOperation(BinaryExpressionType operation)
        {
            return operation is BinaryExpressionType.Add
                or BinaryExpressionType.Subtract
                or BinaryExpressionType.Multiply
                or BinaryExpressionType.Divide
                or BinaryExpressionType.Modulo;
        }

        private static string TypeFamily(string dataType)
        {
            if (IsNumericType(dataType))
            {
                return "numeric";
            }

            if (IsDateOrTimeType(dataType))
            {
                return "datetime";
            }

            if (IsStringType(dataType))
            {
                return "string";
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return "guid";
            }

            return "other";
        }

        private static bool IsDateOrTimeType(string dataType)
        {
            return dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStringType(string dataType)
        {
            if (dataType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return dataType.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetColumnMetadata(
            ColumnReferenceExpression column,
            IReadOnlyList<TableReference> tables,
            out TableColumn metadata)
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;
            var columnName = identifiers?.LastOrDefault()?.Value;

            if (identifiers is null || columnName is null)
            {
                metadata = null!;

                return false;
            }

            var candidates = identifiers.Count == 2
                ? tables.Where(table => table.Matches(identifiers[0].Value))
                : tables;
            var matches = candidates
                .SelectMany(table => metadataByTable.GetValueOrDefault(table.Name) ?? [])
                .Where(item => string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length != 1)
            {
                metadata = null!;

                return false;
            }

            metadata = matches[0];

            return true;
        }
    }
}
