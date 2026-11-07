using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportQueryScopeDiagnostics(
            QuerySpecification query,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlyList<TableReference> tables
        )
        {
            ReportCteAliasShadowing(tables);
            ReportUnknownTables(query, nestedQueries);
        }

        private void ReportUnknownTables(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries)
        {
            if (columnsByTable.Count == 0)
            {
                return;
            }

            foreach (var table in TableReferenceFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries)))
            {
                var tableName = ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema);
                var localName = table.SchemaObject.BaseIdentifier.Value;

                if (IsKnownTable(table, tableName, localName))
                {
                    continue;
                }

                var severity = table.SchemaObject.SchemaIdentifier is null ? "info" : "warning";
                Add("unknown-table", table, $"Table or view '{tableName}' was not found in the workspace index.", severity);
            }
        }

        private bool IsKnownTable(NamedTableReference table, string tableName, string localName)
        {
            return columnsByTable.ContainsKey(tableName)
                || HasLocalColumns(localName, LineOf(table))
                || cteNames.Contains(localName)
                || ObjectNames.IsTemporary(table.SchemaObject);
        }

        private void ValidateQueryColumns(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlySet<ColumnReferenceExpression> orderColumns,
            IReadOnlyList<TableReference> tables
        )
        {
            foreach (var column in ColumnReferenceFinder.Find(node)
                .Where(column => !IsNested(column, nestedQueries) && !orderColumns.Contains(column)))
            {
                ValidateColumn(column, tables);
            }
        }

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
            IReadOnlyList<TableReference> tables
        )
        {
            foreach (var column in orderColumns)
            {
                var identifiers = column.MultiPartIdentifier?.Identifiers;
                if (identifiers?.Count == 1 && selectAliases.Contains(identifiers[0].Value))
                {
                    continue;
                }

                ValidateColumn(column, tables);
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

        private void ReportCteAliasShadowing(IReadOnlyList<TableReference> tables)
        {
            foreach (var table in tables.Where(table => table.Alias is not null && cteNames.Contains(table.Alias)))
            {
                Add("cte-alias-shadowing", table.Node, $"Table alias '{table.Alias}' shadows a CTE with the same name in this query scope.");
            }
        }

        /// <summary>Reports NULL expressions assigned to known non-nullable INSERT SELECT targets.</summary>
        private static bool IsNested(TSqlFragment fragment, IEnumerable<QuerySpecification> nestedQueries) => nestedQueries.Any(query =>
            fragment.StartOffset >= query.StartOffset
            && fragment.StartOffset < query.StartOffset + query.FragmentLength);

        private IReadOnlyList<QuerySpecification> NestedQueries(QuerySpecification node) => QuerySpecificationFinder.Find(node)
            .Where(query => !ReferenceEquals(query, node))
            .ToArray();

        private IReadOnlyList<TableReference> TablesFor(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries)
        {
            var directTables = DirectTables(node, nestedQueries);
            var outerTables = OuterTablesFor(node);

            return directTables.Concat(outerTables).Distinct().ToArray();
        }

        private IEnumerable<TableReference> OuterTablesFor(QuerySpecification node)
        {
            foreach (var query in allQueries.Where(query => Contains(query, node)))
            {
                if (ReferenceEquals(query, node))
                {
                    continue;
                }

                foreach (var table in DirectTables(query, NestedQueries(query)))
                {
                    if (Contains(table.Node, node))
                    {
                        continue;
                    }

                    yield return table with { IsOuterScope = true };
                }
            }
        }

        private IReadOnlyList<TableReference> DirectTables(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries
        )
        {
            return ResolveDirectTables(TableReferenceFinder.Find(node.FromClause), nestedQueries, TryTableReference)
                .Concat(ResolveDirectTables(VariableTableReferenceFinder.Find(node.FromClause), nestedQueries, TryVariableTableReference))
                .Concat(ResolveDirectTables(QueryDerivedTableFinder.Find(node.FromClause), nestedQueries, TryDerivedTableReference))
                .Concat(ResolveDirectTables(SchemaObjectFunctionTableReferenceFinder.Find(node.FromClause), nestedQueries, TryFunctionTableReference))
                .Concat(ResolveDirectTables(OpenJsonTableReferenceFinder.Find(node.FromClause), nestedQueries, TryOpenJsonTableReference))
                .Concat(ResolveDirectTables(PivotedTableReferenceFinder.Find(node.FromClause), nestedQueries, TryPivotedTableReference))
                .Concat(ResolveDirectTables(UnpivotedTableReferenceFinder.Find(node.FromClause), nestedQueries, TryUnpivotedTableReference))
                .ToArray();
        }

        private static IEnumerable<TableReference> ResolveDirectTables<TFragment>(
            IEnumerable<TFragment> candidates,
            IReadOnlyList<QuerySpecification> nestedQueries,
            Func<TFragment, TableReference?> resolve
        ) where TFragment : TSqlFragment
        {
            return candidates
                .Where(candidate => !IsNested(candidate, nestedQueries))
                .Select(resolve)
                .OfType<TableReference>();
        }

        private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
            inner.StartOffset >= outer.StartOffset
            && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;
    }
}
