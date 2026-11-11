using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportOrderByOrdinals(QuerySpecification node)
        {
            foreach (var ordinal in node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<IntegerLiteral>() ?? [])
            {
                if (!int.TryParse(ordinal.Value, out var position))
                {
                    continue;
                }

                if (position >= 1 && position <= node.SelectElements.Count)
                {
                    continue;
                }

                Add("order-by-ordinal", ordinal, $"ORDER BY position {position} is outside the SELECT list (1 to {node.SelectElements.Count}).");
            }
        }

        private void ValidateOrderByColumns(
            IEnumerable<ColumnReferenceExpression> orderColumns,
            IReadOnlySet<string> selectAliases,
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables
        )
        {
            foreach (var column in orderColumns)
            {
                var identifiers = column.MultiPartIdentifier?.Identifiers;
                if (identifiers?.Count == 1 && selectAliases.Contains(identifiers[0].Value))
                {
                    continue;
                }

                ValidateColumn(column, tables, hasUnresolvedDirectTables);
            }
        }

        private void ReportDistinctOrderBy(
            QuerySpecification query,
            IEnumerable<ColumnReferenceExpression> orderColumns,
            HashSet<string> selectAliases
        )
        {
            if (query.UniqueRowFilter != UniqueRowFilter.Distinct)
            {
                return;
            }

            var selectColumnNames = query.SelectElements
                .OfType<SelectScalarExpression>()
                .Select(element => element.Expression as ColumnReferenceExpression)
                .Where(column => column?.MultiPartIdentifier?.Identifiers.Count > 0)
                .Select(column => column!.MultiPartIdentifier!.Identifiers[^1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in orderColumns)
            {
                if (!ShouldReportDistinctOrderByColumn(column, selectAliases, selectColumnNames))
                {
                    continue;
                }

                var columnName = column.MultiPartIdentifier!.Identifiers[0].Value;
                Add("distinct-order-by", column, $"ORDER BY column '{columnName}' must appear in the SELECT list when DISTINCT is used.");
            }
        }

        private static bool ShouldReportDistinctOrderByColumn(
            ColumnReferenceExpression column,
            IReadOnlySet<string> selectAliases,
            IReadOnlySet<string> selectColumnNames
        )
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;

            if (identifiers?.Count != 1)
            {
                return false;
            }

            var columnName = identifiers[0].Value;

            if (selectAliases.Contains(columnName))
            {
                return false;
            }

            return !selectColumnNames.Contains(columnName);
        }
    }
}
