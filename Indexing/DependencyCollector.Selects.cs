using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    private int? SelectElementCount(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => SelectElementCount(specification),
        BinaryQueryExpression binary when SelectElementCount(binary.FirstQueryExpression) is { } first
            && SelectElementCount(binary.SecondQueryExpression) == first => first,
        _ => null,
    };

    private int? SelectElementCount(QuerySpecification specification)
    {
        var count = 0;
        foreach (var element in specification.SelectElements)
        {
            if (element is SelectScalarExpression)
            {
                count++;
                continue;
            }

            if (element is not SelectStarExpression star)
            {
                return null;
            }

            if (SelectStarColumnCount(specification.FromClause, star.Qualifier) is not { } starCount)
            {
                return null;
            }

            count += starCount;
        }

        return count;
    }

    private int? SelectStarColumnCount(FromClause? fromClause, MultiPartIdentifier? qualifier)
    {
        var tables = DirectTableAliasFinder.FindScope(fromClause).Tables;

        if (qualifier is null)
        {
            return SelectUnqualifiedStarColumnCount(tables);
        }

        var matchingTables = tables.Where(table => MatchesTableQualifier(table, qualifier)).ToArray();

        if (matchingTables.Length != 1)
        {
            return null;
        }

        return _temporaryTableColumnCounts.TryGetValue(matchingTables[0].SchemaObject.BaseIdentifier.Value, out var count)
            ? count
            : null;
    }

    private int? SelectUnqualifiedStarColumnCount(IReadOnlyList<NamedTableReference> tables)
    {
        if (!HasKnownTemporaryTableColumns(tables))
        {
            return null;
        }

        return tables.Sum(table => _temporaryTableColumnCounts[table.SchemaObject.BaseIdentifier.Value]);
    }

    private bool HasKnownTemporaryTableColumns(IReadOnlyList<NamedTableReference> tables)
    {
        if (tables.Count == 0)
        {
            return false;
        }

        return tables.All(table => _temporaryTableColumnCounts.ContainsKey(table.SchemaObject.BaseIdentifier.Value));
    }

    private static bool MatchesTableQualifier(NamedTableReference table, MultiPartIdentifier qualifier)
    {
        var name = qualifier.Identifiers.LastOrDefault()?.Value;

        if (name is null)
        {
            return false;
        }

        if (string.Equals(table.Alias?.Value, name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase);
    }
}
