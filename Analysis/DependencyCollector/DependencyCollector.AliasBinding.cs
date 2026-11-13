using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    private void ValidateAliasReference(
        QuerySpecification query,
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases
    )
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null)
        {
            return;
        }

        if (identifiers.Count < 2)
        {
            return;
        }

        var alias = identifiers[0].Value;
        if (variableAliases.Contains(alias))
        {
            return;
        }

        var aliasIsKnown = knownAliases.Contains(alias)
            || IsKnownAlias(tables, alias)
            || IsCorrelatedAlias(query, alias);

        if (!aliasIsKnown)
        {
            if (TableUsingQualifier(tables, identifiers) is { } aliasedTable)
            {
                ReportTableNameAfterAlias(column, identifiers, aliasedTable);

                return;
            }
        }

        if (identifiers.Count == 2)
        {
            ReportTwoPartAliasReference(column, tables, alias, aliasIsKnown);
            return;
        }

        if (aliasIsKnown)
        {
            ReportInvalidMultiPartAliasReference(column, tables, alias);
        }
    }

    private void ReportTwoPartAliasReference(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        bool qualifierIsAlias
    )
    {
        if (qualifierIsAlias || HasUnaliasedTable(tables, qualifier))
        {
            return;
        }

        ReportUnknownAlias(column, qualifier);
    }

    private void ReportTableNameAfterAlias(
        ColumnReferenceExpression column,
        IList<Identifier> identifiers,
        NamedTableReference aliasedTable
    )
    {
        var message = $"The multi-part identifier '{ReferenceText(column)}' could not be bound: "
            + $"table '{QualifierText(identifiers)}' is known as alias '{aliasedTable.Alias!.Value}' in this query.";
        AddLint("table-name-after-alias", "error", column, message);
    }

    private void ReportUnknownAlias(ColumnReferenceExpression column, string qualifier)
    {
        var message = $"The multi-part identifier '{ReferenceText(column)}' could not be bound: "
            + $"'{qualifier}' is not a known table alias in this query.";
        AddLint("unbound-identifier", "error", column, message);
    }

    private void ReportInvalidMultiPartAliasReference(ColumnReferenceExpression column, IReadOnlyList<NamedTableReference> tables, string alias)
    {
        var identifiers = column.MultiPartIdentifier!.Identifiers;
        var nextPartIsAlias = IsKnownAlias(tables, identifiers[1].Value);
        var message = InvalidMultiPartAliasMessage(column, identifiers, alias, nextPartIsAlias);
        AddLint("invalid-alias-reference", "error", column, message);
    }

    private static string InvalidMultiPartAliasMessage(
        ColumnReferenceExpression column,
        IList<Identifier> identifiers,
        string alias,
        bool nextPartIsAlias
    )
    {
        if (nextPartIsAlias)
        {
            return $"Invalid alias reference '{ReferenceText(column)}': '{alias}' is a table alias. A comma may be missing before '{identifiers[1].Value}.{identifiers[^1].Value}'.";
        }

        return $"Invalid alias reference '{ReferenceText(column)}': table alias '{alias}' can qualify only one column.";
    }

    private static bool IsKnownAlias(IEnumerable<NamedTableReference> tables, string alias)
    {
        return tables.Any(table => string.Equals(
            table.Alias?.Value,
            alias,
            StringComparison.OrdinalIgnoreCase
        ));
    }

    private bool IsCorrelatedAlias(QuerySpecification query, string alias)
    {
        if (!_correlatedAliases.TryGetValue(query, out var aliases))
        {
            return false;
        }

        return aliases.Contains(alias);
    }

    private static IReadOnlyDictionary<QuerySpecification, IReadOnlySet<string>> BuildCorrelatedAliases(TSqlStatement statement)
    {
        var queries = QuerySpecificationFinder.Find(statement);
        var aliasesByQuery = queries.ToDictionary(
            query => query,
            query => DirectTableAliasFinder.FindScope(query.FromClause).Aliases
                .Select(reference => reference.Alias?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        var correlatedAliases = new Dictionary<QuerySpecification, IReadOnlySet<string>>();

        foreach (var query in queries)
        {
            correlatedAliases[query] = CorrelatedAliasesFor(query, queries, aliasesByQuery);
        }

        return correlatedAliases;
    }

    private static IReadOnlySet<string> CorrelatedAliasesFor(
        QuerySpecification query,
        IReadOnlyList<QuerySpecification> queries,
        IReadOnlyDictionary<QuerySpecification, HashSet<string>> aliasesByQuery
    )
    {
        return queries
            .Where(parent => parent != query && ContainsQuery(parent, query))
            .SelectMany(parent => aliasesByQuery[parent])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsQuery(TSqlFragment outer, TSqlFragment inner)
    {
        int outerEnd = outer.StartOffset + outer.FragmentLength;
        int innerEnd = inner.StartOffset + inner.FragmentLength;

        if (inner.StartOffset < outer.StartOffset)
        {
            return false;
        }

        return innerEnd <= outerEnd;
    }

    private NamedTableReference? TableUsingQualifier(IEnumerable<NamedTableReference> tables, IList<Identifier> identifiers)
    {
        string qualifier = QualifierText(identifiers);

        return tables.FirstOrDefault(table => MatchesTableNameAfterAlias(table, qualifier));
    }

    private bool MatchesTableNameAfterAlias(NamedTableReference table, string qualifier)
    {
        if (table.Alias is null)
        {
            return false;
        }

        string fullName = ObjectNames.Format(table.SchemaObject, defaultSchema, defaultDatabase);

        if (string.Equals(fullName, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(table.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnaliasedTable(IEnumerable<NamedTableReference> tables, string name)
    {
        return tables.Any(table => IsUnaliasedTable(table, name));
    }

    private static bool IsUnaliasedTable(NamedTableReference table, string name)
    {
        if (table.Alias is not null)
        {
            return false;
        }

        return string.Equals(
            table.SchemaObject.BaseIdentifier.Value,
            name,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReferenceText(ColumnReferenceExpression column)
    {
        return string.Join(
            '.',
            column.MultiPartIdentifier!.Identifiers.Select(identifier => identifier.Value)
        );
    }

    private static string QualifierText(IList<Identifier> identifiers)
    {
        return string.Join(
            '.',
            identifiers.Take(identifiers.Count - 1).Select(identifier => identifier.Value)
        );
    }

}
