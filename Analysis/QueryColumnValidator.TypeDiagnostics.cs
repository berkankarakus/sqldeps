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

            if (expression is NumericLiteral or IntegerLiteral or MoneyLiteral)
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
