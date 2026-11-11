using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

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
            if (columnsByTable.ContainsKey(tableName))
            {
                return true;
            }

            if (HasLocalColumns(localName, LineOf(table)))
            {
                return true;
            }

            if (cteNames.Contains(localName))
            {
                return true;
            }

            return ObjectNames.IsTemporary(table.SchemaObject);
        }

        private void ValidateQueryColumns(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlySet<ColumnReferenceExpression> orderColumns,
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables
        )
        {
            foreach (var column in ColumnReferenceFinder.Find(node)
                .Where(column => !IsNested(column, nestedQueries) && !orderColumns.Contains(column)))
            {
                ValidateColumn(column, tables, hasUnresolvedDirectTables);
            }
        }

        private bool HasUnresolvedDirectTables(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries
        )
        {
            foreach (var table in TableReferenceFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries)))
            {
                var tableName = ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema);
                var localName = table.SchemaObject.BaseIdentifier.Value;

                if (!IsKnownTable(table, tableName, localName))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportCteAliasShadowing(IReadOnlyList<TableReference> tables)
        {
            foreach (var table in tables.Where(table => table.Alias is not null && cteNames.Contains(table.Alias)))
            {
                Add("cte-alias-shadowing", table.Node, $"Table alias '{table.Alias}' shadows a CTE with the same name in this query scope.");
            }
        }

        /// <summary>Reports NULL expressions assigned to known non-nullable INSERT SELECT targets.</summary>
        private static bool IsNested(TSqlFragment fragment, IEnumerable<QuerySpecification> nestedQueries)
        {
            return nestedQueries.Any(query =>
                fragment.StartOffset >= query.StartOffset
                && fragment.StartOffset < query.StartOffset + query.FragmentLength);
        }

        private IReadOnlyList<QuerySpecification> NestedQueries(QuerySpecification node)
        {
            return allQueries
                .Where(query => !ReferenceEquals(query, node) && Contains(node, query))
                .ToArray();
        }

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

        private static bool Contains(TSqlFragment outer, TSqlFragment inner)
        {
            var outerEnd = outer.StartOffset + outer.FragmentLength;
            var innerEnd = inner.StartOffset + inner.FragmentLength;

            return inner.StartOffset >= outer.StartOffset && innerEnd <= outerEnd;
        }
    }
}
