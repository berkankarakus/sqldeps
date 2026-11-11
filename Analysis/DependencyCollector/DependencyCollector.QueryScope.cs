using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(QualifiedJoin node)
    {
        if (node.SearchCondition is null
            || ConstantExpressionEvaluator.Evaluate(node.SearchCondition) != true)
        {
            return;
        }

        AddLint(
            "join-ineffective-condition",
            "info",
            node.SearchCondition,
            "JOIN condition is always true and does not restrict matches; verify that a Cartesian-like join is intentional."
        );
    }

    public override void Visit(QuerySpecification node)
    {
        var scope = DirectTableAliasFinder.FindScope(node.FromClause);
        var tables = scope.Tables;
        var aliases = scope.Aliases;
        ReportUnfilteredQuery(node, tables);

        var variableAliases = VariableAliases(node);
        var knownAliases = KnownAliases(aliases, variableAliases);

        ReportDuplicateAliases(aliases);
        ReportUngroupedSelectColumns(node);
        AddSelectColumnUsages(node, tables, knownAliases, variableAliases);
    }

    private static HashSet<string> VariableAliases(QuerySpecification query)
    {
        return VariableTableReferenceFinder.Find(query.FromClause)
            .Select(reference => reference.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> KnownAliases(
        IReadOnlyList<TableReferenceWithAlias> aliases,
        IReadOnlySet<string> variableAliases)
    {
        var knownAliases = aliases
            .Select(table => table.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        knownAliases.UnionWith(variableAliases);

        return knownAliases;
    }

    private void AddSelectColumnUsages(
        QuerySpecification query,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases)
    {
        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
        {
            AddSelectColumnUsage(query, element, tables, knownAliases, variableAliases);
        }
    }

    private void AddSelectColumnUsage(
        QuerySpecification query,
        SelectScalarExpression element,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases)
    {
        if (element.Expression is not ColumnReferenceExpression column)
        {
            return;
        }

        ValidateAliasReference(query, column, tables, knownAliases, variableAliases);

        if (!TryResolveColumnTable(column, tables, out var table))
        {
            return;
        }

        if (!TryGetColumnName(column, out var name))
        {
            return;
        }

        AddColumnUsage(table, name, "SELECT", column);
    }

    private void ReportDuplicateAliases(IReadOnlyList<TableReferenceWithAlias> tables)
    {
        foreach (var duplicates in tables
            .Where(table => table.Alias is not null)
            .GroupBy(table => table.Alias!.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var duplicate in duplicates.Skip(1))
            {
                AddLint("duplicate-alias", "error", duplicate.Alias!, $"Duplicate table alias '{duplicates.Key}' in this query scope.");
            }
        }
    }

    private static bool TryGetColumnName(ColumnReferenceExpression? column, out string name)
    {
        if (column is null)
        {
            name = "";
            return false;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null)
        {
            name = "";
            return false;
        }

        if (identifiers.Count == 0)
        {
            name = "";
            return false;
        }

        name = identifiers[identifiers.Count - 1].Value;
        return true;
    }

}
