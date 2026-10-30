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
