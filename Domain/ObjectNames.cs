using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Domain;

/// <summary>Writes object names the one way the index stores and queries them: schema.name.</summary>
public static class ObjectNames
{
    /// <summary>The schema a name written without one is read as, unless told otherwise (--default-schema).</summary>
    public const string StandardDefaultSchema = "dbo";

    /// <summary>The marker for SQL built at run time (EXEC (@sql), sp_executesql): its targets are unknown.</summary>
    public const string DynamicSql = "(dynamic sql)";

    /// <summary>Returns the final identifier from a dot-qualified name.</summary>
    public static string BaseName(string name)
    {
        if (TrySplitMultipartIdentifier(name, out var parts) && parts.Length > 0)
        {
            return parts[^1];
        }

        var separator = name.LastIndexOf('.');

        return separator < 0 ? name : name[(separator + 1)..];
    }

    /// <summary>Qualifies a textual one-, two-, or already fully qualified object name.</summary>
    public static string Qualify(
        string name,
        string defaultSchema,
        string? defaultDatabase = null
    )
    {
        if (!TrySplitMultipartIdentifier(name, out var parts))
        {
            return name;
        }

        if (parts.Length == 0)
        {
            return name;
        }

        if (parts.Length >= 3)
        {
            return string.Join('.', parts);
        }

        if (parts.Length == 2)
        {
            return defaultDatabase is null
                ? string.Join('.', parts)
                : $"{defaultDatabase}.{parts[0]}.{parts[1]}";
        }

        return defaultDatabase is null
            ? $"{defaultSchema}.{parts[0]}"
            : $"{defaultDatabase}.{defaultSchema}.{parts[0]}";
    }

    private static bool TrySplitMultipartIdentifier(string name, out string[] parts)
    {
        List<string> values = [];
        StringBuilder current = new();
        var insideBrackets = false;

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];

            if (insideBrackets)
            {
                if (character == ']' && index + 1 < name.Length && name[index + 1] == ']')
                {
                    current.Append(']');
                    index++;

                    continue;
                }

                if (character == ']')
                {
                    insideBrackets = false;

                    continue;
                }

                current.Append(character);

                continue;
            }

            if (character == '[')
            {
                insideBrackets = true;

                continue;
            }

            if (character == '.')
            {
                if (!AddPart(values, current))
                {
                    parts = [];

                    return false;
                }

                continue;
            }

            if (character == ']')
            {
                parts = [];

                return false;
            }

            current.Append(character);
        }

        if (insideBrackets || !AddPart(values, current))
        {
            parts = [];

            return false;
        }

        parts = values.ToArray();

        return true;
    }

    private static bool AddPart(ICollection<string> parts, StringBuilder current)
    {
        var value = current.ToString().Trim();
        current.Clear();

        if (value.Length == 0)
        {
            return false;
        }

        parts.Add(value);

        return true;
    }

    /// <summary>
    /// dbo.Prices, or Archive.dbo.Prices when the name carries a database. A name written without a
    /// schema is read as the default schema, the same way SQL Server resolves it for most users.
    /// When a default database is supplied, names without an explicit database are stored in that
    /// database's three-part form.
    /// </summary>
    public static string Format(
        SchemaObjectName name,
        string defaultSchema,
        string? defaultDatabase = null
    )
    {
        var schema = name.SchemaIdentifier is null
            ? defaultSchema
            : name.SchemaIdentifier.Value;

        var qualified = $"{schema}.{name.BaseIdentifier.Value}";

        if (name.DatabaseIdentifier is null && name.ServerIdentifier is null && defaultDatabase is not null)
        {
            return $"{defaultDatabase}.{qualified}";
        }

        if (name.DatabaseIdentifier is null)
        {
            return qualified;
        }

        var withDatabase = $"{name.DatabaseIdentifier.Value}.{qualified}";

        if (name.ServerIdentifier is null)
        {
            return withDatabase;
        }

        return $"{name.ServerIdentifier.Value}.{withDatabase}";
    }

    /// <summary>The "inserted" and "deleted" tables a trigger reads are the rows being changed, not objects.</summary>
    public static bool IsTriggerPseudoTable(SchemaObjectName name)
    {
        if (name.SchemaIdentifier is not null)
        {
            return false;
        }

        var baseName = name.BaseIdentifier.Value;

        return
            string.Equals(baseName, "inserted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(baseName, "deleted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Temporary tables (#t, ##t) live and die inside one script; they are not dependencies.</summary>
    public static bool IsTemporary(SchemaObjectName name)
    {
        return name.BaseIdentifier.Value.StartsWith('#');
    }
}
