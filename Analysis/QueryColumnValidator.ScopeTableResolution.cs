using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        /// <summary>Checks whether a fragment belongs to one of the nested queries.</summary>
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
            IReadOnlyList<QuerySpecification> nestedQueries)
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
            Func<TFragment, TableReference?> resolve)
            where TFragment : TSqlFragment
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
