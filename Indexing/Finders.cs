using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>
/// The names of common table expressions in a batch. A CTE is read like a table in FROM, but it is
/// not an object, so its uses must not show up as dependencies. Collected before the main walk
/// because a use can be visited before its definition.
/// </summary>
public sealed class CteNameCollector : TSqlFragmentVisitor
{
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public static HashSet<string> Collect(TSqlFragment fragment)
    {
        CteNameCollector collector = new();
        fragment.Accept(collector);

        return collector._names;
    }

    public override void Visit(CommonTableExpression node)
    {
        _names.Add(node.ExpressionName.Value);
    }
}

public sealed class TryCatchStatementFinder : TSqlFragmentVisitor
{
    private readonly List<TryCatchStatement> _statements = [];

    public static IReadOnlyList<TryCatchStatement> Find(TSqlFragment fragment)
    {
        TryCatchStatementFinder finder = new();
        fragment.Accept(finder);

        return finder._statements;
    }

    public override void Visit(TryCatchStatement node) => _statements.Add(node);
}

/// <summary>Finds every query specification so correlated aliases can be resolved once per statement.</summary>
public sealed class QuerySpecificationFinder : TSqlFragmentVisitor
{
    private readonly List<QuerySpecification> _queries = [];

    public static IReadOnlyList<QuerySpecification> Find(TSqlFragment? fragment)
    {
        QuerySpecificationFinder finder = new();
        fragment?.Accept(finder);

        return finder._queries;
    }

    public override void Visit(QuerySpecification node) => _queries.Add(node);
}

public sealed class RollbackTransactionFinder : TSqlFragmentVisitor
{
    public bool Found { get; private set; }

    public override void Visit(RollbackTransactionStatement node) => Found = true;
}

/// <summary>Every table reference under a fragment, used to find what an UPDATE or DELETE alias stands for.</summary>
public sealed class TableReferenceFinder : TSqlFragmentVisitor
{
    private readonly List<NamedTableReference> _references = [];

    public static IReadOnlyList<NamedTableReference> Find(TSqlFragment? fragment)
    {
        TableReferenceFinder finder = new();

        if (fragment is not null)
        {
            fragment.Accept(finder);
        }

        return finder._references;
    }

    public override void Visit(NamedTableReference node)
    {
        _references.Add(node);
    }
}

public sealed class VariableTableReferenceFinder : TSqlFragmentVisitor
{
    private readonly List<VariableTableReference> _references = [];

    public static IReadOnlyList<VariableTableReference> Find(TSqlFragment? fragment)
    {
        VariableTableReferenceFinder finder = new();
        fragment?.Accept(finder);

        return finder._references;
    }

    public override void Visit(VariableTableReference node) => _references.Add(node);
}

public sealed class QueryDerivedTableFinder : TSqlFragmentVisitor
{
    private readonly List<QueryDerivedTable> _tables = [];

    public static IReadOnlyList<QueryDerivedTable> Find(TSqlFragment? fragment)
    {
        QueryDerivedTableFinder finder = new();
        fragment?.Accept(finder);

        return finder._tables;
    }

    public override void Visit(QueryDerivedTable node) => _tables.Add(node);
}

public sealed class DirectQueryDerivedTableFinder : TSqlFragmentVisitor
{
    private readonly List<QueryDerivedTable> _tables = [];

    public static IReadOnlyList<QueryDerivedTable> Find(TSqlFragment? fragment)
    {
        DirectQueryDerivedTableFinder finder = new();
        fragment?.Accept(finder);

        return finder._tables;
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (!_tables.Any(table => Contains(table, node)))
        {
            _tables.Add(node);
        }
    }

    private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
        inner.StartOffset >= outer.StartOffset
        && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;
}

/// <summary>Table aliases declared directly in one query scope.</summary>
public sealed class DirectTableAliasFinder : TSqlFragmentVisitor
{
    private readonly List<NamedTableReference> _tables = [];
    private readonly List<TableReferenceWithAlias> _references = [];
    private readonly List<QueryDerivedTable> _derivedTables = [];

    public static IReadOnlyList<TableReferenceWithAlias> Find(TSqlFragment? fragment)
    {
        return FindScope(fragment).Aliases;
    }

    public static DirectTableScope FindScope(TSqlFragment? fragment)
    {
        DirectTableAliasFinder finder = new();
        fragment?.Accept(finder);

        return new DirectTableScope(finder._tables, finder._references);
    }

    public override void Visit(NamedTableReference node)
    {
        if (!IsNested(node))
        {
            _tables.Add(node);
            Add(node);
        }
    }

    public override void Visit(VariableTableReference node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    public override void Visit(OpenJsonTableReference node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    public override void Visit(OpenQueryTableReference node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    public override void Visit(FullTextTableReference node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }

        _derivedTables.Add(node);
    }

    public override void Visit(InlineDerivedTable node)
    {
        if (!IsNested(node))
        {
            Add(node);
        }
    }

    private void Add(TableReferenceWithAlias node)
    {
        if (node.Alias is not null)
        {
            _references.Add(node);
        }
    }

    private bool IsNested(TSqlFragment node) => _derivedTables.Any(table => Contains(table, node));

    private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
        inner.StartOffset >= outer.StartOffset
        && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;
}

/// <summary>Direct tables and aliases collected from one query scope.</summary>
/// <param name="Tables">Named table references in the scope.</param>
/// <param name="Aliases">Table references that declare aliases in the scope.</param>
public sealed record DirectTableScope(
    IReadOnlyList<NamedTableReference> Tables,
    IReadOnlyList<TableReferenceWithAlias> Aliases
);
