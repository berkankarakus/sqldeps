using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Domain;

/// <summary>Writes object names the one way the index stores and queries them: schema.name.</summary>
public static class ObjectNames
{
    /// <summary>The schema a name written without one is read as, unless told otherwise (--default-schema).</summary>
    public const string StandardDefaultSchema = "dbo";

    /// <summary>The marker for SQL built at run time (EXEC (@sql), sp_executesql): its targets are unknown.</summary>
    public const string DynamicSql = "(dynamic sql)";

    /// <summary>
    /// dbo.Prices, or Archive.dbo.Prices when the name carries a database. A name written without a
    /// schema is read as the default schema, the same way SQL Server resolves it for most users.
    /// </summary>
    public static string Format(SchemaObjectName name, string defaultSchema)
    {
        var schema = name.SchemaIdentifier is null
            ? defaultSchema
            : name.SchemaIdentifier.Value;

        var qualified = $"{schema}.{name.BaseIdentifier.Value}";

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
