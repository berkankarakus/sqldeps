using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed class JoinComparisonVisitor : TSqlFragmentVisitor
    {
        public List<BooleanComparisonExpression> Comparisons { get; } = [];

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType == BooleanComparisonType.Equals)
            {
                Comparisons.Add(node);
            }
        }
    }

    private sealed record TableReference(string? Alias, string Name, IReadOnlySet<string> Columns, TSqlFragment Node, bool IsOuterScope = false)
    {
        public string Qualifier => Alias ?? Name.Split('.').Last();

        public bool Matches(string qualifier)
        {
            if (string.Equals(Alias, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Alias is not null)
            {
                return false;
            }

            return string.Equals(Name.Split('.').Last(), qualifier, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ColumnReferenceFinder : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> References { get; } = [];

        public static IEnumerable<ColumnReferenceExpression> Find(TSqlFragment fragment)
        {
            ColumnReferenceFinder finder = new();
            fragment.Accept(finder);
            return finder.References;
        }

        public override void Visit(ColumnReferenceExpression node) => References.Add(node);
    }

    private sealed class QuerySpecificationFinder : TSqlFragmentVisitor
    {
        public List<QuerySpecification> Queries { get; } = [];

        public static IEnumerable<QuerySpecification> Find(TSqlFragment fragment)
        {
            QuerySpecificationFinder finder = new();
            fragment.Accept(finder);
            return finder.Queries;
        }

        public override void Visit(QuerySpecification node) => Queries.Add(node);
    }

    private sealed class QueryDerivedTableFinder : TSqlFragmentVisitor
    {
        public List<QueryDerivedTable> Tables { get; } = [];

        public static IEnumerable<QueryDerivedTable> Find(TSqlFragment? fragment)
        {
            QueryDerivedTableFinder finder = new();
            fragment?.Accept(finder);
            return finder.Tables;
        }

        public override void Visit(QueryDerivedTable node) => Tables.Add(node);
    }

    private sealed class SchemaObjectFunctionTableReferenceFinder : TSqlFragmentVisitor
    {
        public List<SchemaObjectFunctionTableReference> Tables { get; } = [];

        public static IEnumerable<SchemaObjectFunctionTableReference> Find(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return [];
            }

            SchemaObjectFunctionTableReferenceFinder finder = new();
            fragment.Accept(finder);

            return finder.Tables;
        }

        public override void Visit(SchemaObjectFunctionTableReference node) => Tables.Add(node);
    }

    private sealed class OpenJsonTableReferenceFinder : TSqlFragmentVisitor
    {
        public List<OpenJsonTableReference> Tables { get; } = [];

        public static IEnumerable<OpenJsonTableReference> Find(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return [];
            }

            OpenJsonTableReferenceFinder finder = new();
            fragment.Accept(finder);

            return finder.Tables;
        }

        public override void Visit(OpenJsonTableReference node) => Tables.Add(node);
    }

    private sealed class PivotedTableReferenceFinder : TSqlFragmentVisitor
    {
        public List<PivotedTableReference> Tables { get; } = [];

        public static IEnumerable<PivotedTableReference> Find(TSqlFragment? fragment)
        {
            PivotedTableReferenceFinder finder = new();
            fragment?.Accept(finder);

            return finder.Tables;
        }

        public override void Visit(PivotedTableReference node) => Tables.Add(node);
    }

    private sealed class UnpivotedTableReferenceFinder : TSqlFragmentVisitor
    {
        public List<UnpivotedTableReference> Tables { get; } = [];

        public static IEnumerable<UnpivotedTableReference> Find(TSqlFragment? fragment)
        {
            UnpivotedTableReferenceFinder finder = new();
            fragment?.Accept(finder);

            return finder.Tables;
        }

        public override void Visit(UnpivotedTableReference node) => Tables.Add(node);
    }
}
