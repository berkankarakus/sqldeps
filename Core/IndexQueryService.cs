using SqlDeps.Storage;

namespace SqlDeps.Core;

/// <summary>
/// Safe, named read operations over an index. CLI and future protocol adapters such as MCP use this
/// boundary instead of owning SQL strings or accepting arbitrary SQL from callers.
/// </summary>
public sealed class IndexQueryService(IndexDatabase database)
{
    private static readonly IReadOnlyDictionary<string, string> ObjectQueries = new Dictionary<string, string>
    {
        ["who-writes"] = "SELECT DISTINCT source_object, operation, source_file, line FROM writers WHERE target_object = $name COLLATE NOCASE ORDER BY source_object, operation, source_file, line",
        ["who-reads"] = "SELECT DISTINCT source_object, source_file, line FROM readers WHERE target_object = $name COLLATE NOCASE ORDER BY source_object, source_file, line",
        ["deps"] = "SELECT DISTINCT target_object, operation, source_file, line FROM dependencies WHERE source_object = $name COLLATE NOCASE ORDER BY target_object, operation, source_file, line",
        ["impact"] = "SELECT affected, depth, path FROM impact WHERE origin = $name COLLATE NOCASE ORDER BY depth, affected",
        ["columns"] = "SELECT DISTINCT table_name, column_name, operation, source_file, line FROM column_usage WHERE source_object = $name COLLATE NOCASE ORDER BY source_file, line, operation, table_name, column_name",
    };

    private static readonly IReadOnlyDictionary<string, string> ListQueries = new Dictionary<string, string>
    {
        ["objects"] = "SELECT DISTINCT name, kind, source_file, line FROM objects ORDER BY name, kind, source_file, line",
        ["duplicates"] = "SELECT name, kind, definitions, places FROM duplicate_definitions ORDER BY name, kind",
        ["errors"] = "SELECT source_file, line, message FROM parse_errors ORDER BY source_file, line",
        ["warnings"] = "SELECT source_file, line, message FROM parse_warnings ORDER BY source_file, line",
    };

    private const string LintQuery = """
        SELECT DISTINCT severity, rule, object_name, source_file, line, message
        FROM (
            SELECT severity, rule, source_object AS object_name, source_file, line, message
            FROM lint_findings
            UNION ALL
            SELECT 'error' AS severity, 'parse-error' AS rule,
                   source_file AS object_name, source_file, line, message
            FROM parse_errors
            UNION ALL
            SELECT 'warning', 'dynamic-sql', source_object, source_file, line,
                   'Uses unresolved dynamic SQL; dependency analysis may be incomplete.'
            FROM dependencies
            WHERE operation = 'EXEC_DYNAMIC'
            UNION ALL
            SELECT 'warning', 'duplicate-definition', name, '', 0,
                   'Has ' || definitions || ' definitions: ' || places
            FROM duplicate_definitions
            UNION ALL
            SELECT 'info', 'unused-object', name, source_file, line,
                   'Defined in the index but not referenced by another indexed object.'
            FROM unused_objects
            UNION ALL
            SELECT DISTINCT 'info', 'cross-database-dependency', source_object, source_file, line,
                   'References ' || target_object || ' outside the current database.'
            FROM dependencies
            WHERE length(target_object) - length(replace(target_object, '.', '')) >= 2
        )
        ORDER BY CASE severity WHEN 'error' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END,
                 rule, object_name, source_file, line
        """;

    public static bool IsObjectQuery(string command) => ObjectQueries.ContainsKey(command);

    public static bool IsListQuery(string command) => ListQueries.ContainsKey(command);

    public List<Dictionary<string, object?>> ObjectQuery(string command, string objectName, int? limit = null)
    {
        if (!ObjectQueries.TryGetValue(command, out var query))
        {
            throw new ArgumentException("Unknown object query: " + command, nameof(command));
        }

        var qualifiedName = database.QualifyObjectName(objectName);
        return database.Query(WithLimit(query, limit), ("$name", qualifiedName), ("$limit", limit));
    }

    public List<Dictionary<string, object?>> ListQuery(string command, int? limit = null)
    {
        if (!ListQueries.TryGetValue(command, out var query))
        {
            throw new ArgumentException("Unknown list query: " + command, nameof(command));
        }

        return database.Query(WithLimit(query, limit), ("$limit", limit));
    }

    public List<Dictionary<string, object?>> WhoUpdates(string tableName, string columnName, int? limit = null)
    {
        const string query = """
            SELECT DISTINCT source_object, operation, source_file, line
            FROM column_usage
            WHERE table_name = $table COLLATE NOCASE
              AND column_name = $column COLLATE NOCASE
              AND operation = 'UPDATE'
            ORDER BY source_object, source_file, line
            """;

        var qualifiedTableName = database.QualifyObjectName(tableName);
        return database.Query(
            WithLimit(query, limit),
            ("$table", qualifiedTableName),
            ("$column", columnName),
            ("$limit", limit)
        );
    }

    public List<Dictionary<string, object?>> Lint(int? limit = null)
    {
        return database.Query(WithLimit(LintQuery, limit), ("$limit", limit));
    }

    private static string WithLimit(string sql, int? limit) => limit is null ? sql : sql + " LIMIT $limit";
}
