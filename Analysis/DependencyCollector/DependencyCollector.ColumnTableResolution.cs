using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    private static bool TryResolveColumnTable(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        out NamedTableReference table)
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;

        if (identifiers is null)
        {
            table = null!;

            return false;
        }

        if (identifiers.Count == 0)
        {
            table = null!;

            return false;
        }

        if (identifiers.Count == 1 && tables.Count == 1)
        {
            table = tables[0];

            return true;
        }

        if (!TryGetQualifier(identifiers, out var qualifier))
        {
            table = null!;

            return false;
        }

        return TryFindUniqueTable(tables, qualifier, out table);
    }

    private static bool TryGetQualifier(IList<Identifier> identifiers, out string qualifier)
    {
        if (identifiers.Count < 2)
        {
            qualifier = "";

            return false;
        }

        qualifier = identifiers[identifiers.Count - 2].Value;

        return true;
    }

    private static bool TryFindUniqueTable(
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        out NamedTableReference table)
    {
        table = null!;

        foreach (var reference in tables.Where(reference => MatchesQualifier(reference, qualifier)))
        {
            if (table is not null)
            {
                table = null!;

                return false;
            }

            table = reference;
        }

        return table is not null;
    }

    private static bool MatchesQualifier(NamedTableReference reference, string qualifier)
    {
        if (string.Equals(reference.Alias?.Value, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(reference.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);
    }
}
