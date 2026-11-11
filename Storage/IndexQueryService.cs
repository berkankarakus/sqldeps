using SqlDeps;
using SqlDeps.Domain;

namespace SqlDeps.Storage;

/// <summary>
/// Safe, named read operations over an index. CLI and future protocol adapters such as MCP use this
/// boundary instead of owning SQL strings or accepting arbitrary SQL from callers.
/// </summary>
public sealed class IndexQueryService(IndexDatabase database)
{
    /// <summary>Returns whether the indexed source folder still matches the stored file hashes.</summary>
    public bool IsIndexCurrent()
    {
        string? sourceRoot = database.SourceRoot;

        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return false;
        }

        return database.GetStatus(sourceRoot).IsCurrent;
    }

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
            SELECT 'warning', 'duplicate-definition', duplicate.name,
                   first_definition.source_file, first_definition.line,
                   'Has ' || duplicate.definitions || ' definitions: ' || duplicate.places
            FROM duplicate_definitions duplicate
            JOIN objects first_definition
              ON first_definition.name = duplicate.name COLLATE NOCASE
             AND first_definition.kind = duplicate.kind
            WHERE NOT EXISTS (
                SELECT 1
                FROM objects earlier_definition
                WHERE earlier_definition.name = first_definition.name COLLATE NOCASE
                  AND earlier_definition.kind = first_definition.kind
                  AND (
                      earlier_definition.source_file < first_definition.source_file
                      OR (
                          earlier_definition.source_file = first_definition.source_file
                          AND earlier_definition.line < first_definition.line
                      )
                  )
            )
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

    /// <summary>Returns whether <paramref name="command" /> is a parameterized object query.</summary>
    public static bool IsObjectQuery(string command) => ObjectQueries.ContainsKey(command);

    /// <summary>Returns whether <paramref name="command" /> is a parameterized list query.</summary>
    public static bool IsListQuery(string command) => ListQueries.ContainsKey(command);

    /// <summary>Qualifies an object name with the schema recorded by the index.</summary>
    public string QualifyObjectName(string name)
    {
        return database.QualifyObjectName(name);
    }

    /// <summary>Loads the metadata required for in-memory document analysis.</summary>
    public IndexAnalysisMetadata LoadAnalysisMetadata()
    {
        IReadOnlyCollection<CachedFile> files = IndexCache
            .Load(database.Path, IndexCache.SettingsFor(database.DefaultSchema, database.SourceRoot))
            .Values;

        return new IndexAnalysisMetadata(
            new MetadataCatalog(
                files.SelectMany(file => file.ResultColumns),
                files.SelectMany(file => file.TableColumns),
                files.SelectMany(file => file.TableKeys),
                files.SelectMany(file => file.ForeignKeys),
                files.SelectMany(file => file.IndexColumns)),
            files.SelectMany(file => file.Objects).ToArray(),
            files.SelectMany(file => file.Parameters).ToArray());
    }

    /// <summary>Runs one of the allow-listed object queries.</summary>
    public List<Dictionary<string, object?>> ObjectQuery(string command, string objectName, int? limit = null)
    {
        if (!ObjectQueries.TryGetValue(command, out var query))
        {
            throw new ArgumentException("Unknown object query: " + command, nameof(command));
        }

        var qualifiedName = database.QualifyObjectName(objectName);
        return database.Query(WithLimit(query, limit), ("$name", qualifiedName), ("$limit", limit));
    }

    /// <summary>Runs one of the allow-listed list queries.</summary>
    public List<Dictionary<string, object?>> ListQuery(string command, int? limit = null)
    {
        if (!ListQueries.TryGetValue(command, out var query))
        {
            throw new ArgumentException("Unknown list query: " + command, nameof(command));
        }

        return database.Query(WithLimit(query, limit), ("$limit", limit));
    }

    /// <summary>Finds indexed UPDATE writers for one table column.</summary>
    /// <param name="tableName">The table whose column is being inspected.</param>
    /// <param name="columnName">The column whose writers are requested.</param>
    /// <param name="limit">An optional maximum row count.</param>
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

    /// <summary>Finds indexed objects whose names contain a case-insensitive pattern.</summary>
    /// <param name="pattern">The object-name fragment to search for.</param>
    /// <param name="limit">An optional maximum row count.</param>
    public List<Dictionary<string, object?>> FindObjects(string pattern, int? limit = null)
    {
        const string query = """
            SELECT DISTINCT name, kind, source_file, line
            FROM objects
            WHERE name LIKE $pattern ESCAPE '!' COLLATE NOCASE
            ORDER BY name, kind, source_file, line
            """;

        return database.Query(
            WithLimit(query, limit),
            ("$pattern", "%" + EscapeLike(pattern) + "%"),
            ("$limit", limit)
        );
    }

    /// <summary>Returns the indexed definition locations for one object.</summary>
    /// <param name="objectName">The object name to locate.</param>
    public List<Dictionary<string, object?>> Source(string objectName)
    {
        const string query = """
            SELECT DISTINCT kind, source_file, line
            FROM objects
            WHERE name = $name COLLATE NOCASE
            ORDER BY source_file, line
            """;

        return database.Query(query, ("$name", database.QualifyObjectName(objectName)));
    }

    /// <summary>Returns direct dependency edges for graph export.</summary>
    /// <param name="limit">An optional maximum number of edges.</param>
    public List<Dictionary<string, object?>> GraphEdges(int? limit = null)
    {
        const string query = """
            SELECT DISTINCT source_object, target_object, operation, source_file, line, via_dynamic
            FROM dependencies
            WHERE source_object <> '(batch)' AND target_object <> '(dynamic sql)'
            ORDER BY source_object, target_object, operation, source_file, line
            """;

        return database.Query(WithLimit(query, limit), ("$limit", limit));
    }

    /// <summary>Returns indexed column metadata, including key and foreign-key markers.</summary>
    /// <param name="tableName">The table or view whose columns are requested.</param>
    /// <param name="limit">An optional maximum row count.</param>
    public List<Dictionary<string, object?>> TableColumns(string tableName, int? limit = null)
    {
        const string query = """
            SELECT column_name, data_type, is_nullable, is_identity, default_value, is_computed,
                   precision, scale, collation,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM table_keys
                       WHERE table_name = table_columns.table_name
                         AND column_name = table_columns.column_name
                         AND kind = 'primary-key'
                   ) THEN 'PK' ELSE '' END AS primary_key,
                   COALESCE((
                       SELECT group_concat(referenced_table_name || '.' || referenced_column_name, ', ')
                       FROM foreign_keys
                       WHERE table_name = table_columns.table_name
                         AND column_name = table_columns.column_name
                   ), '') AS foreign_key,
                   source_file, line
            FROM table_columns
            WHERE table_name = $table COLLATE NOCASE
            ORDER BY column_name, source_file, line
            """;

        return database.Query(
            WithLimit(query, limit),
            ("$table", database.QualifyObjectName(tableName)),
            ("$limit", limit)
        );
    }

    /// <summary>Returns one indexed table or view by its qualified name.</summary>
    public IndexedObjectMetadata? FindTableOrView(string objectName)
    {
        string qualifiedName = database.QualifyObjectName(objectName);
        Dictionary<string, object?>? row = database.Query(
                "SELECT name, kind FROM objects WHERE name = $name COLLATE NOCASE AND kind IN ('table', 'view') ORDER BY kind, name LIMIT 1",
                ("$name", qualifiedName))
            .SingleOrDefault();

        if (row is null)
        {
            return null;
        }

        string storedName = TextOrDefault(row, "name", qualifiedName);
        string storedKind = TextOrDefault(row, "kind", "table");

        return new IndexedObjectMetadata(
            storedName,
            storedKind);
    }

    /// <summary>Returns indexed columns in stored ordinal order.</summary>
    public IReadOnlyList<string> ColumnsFor(string objectName)
    {
        return database.Query(
                "SELECT column_name FROM table_columns WHERE table_name = $name COLLATE NOCASE ORDER BY rowid",
                ("$name", objectName))
            .Select(row => TextOrDefault(row, "column_name", ""))
            .ToArray();
    }

    /// <summary>Returns indexed foreign-key relations for one table or view.</summary>
    public IReadOnlyList<IndexedForeignKey> ForeignKeysFor(string objectName)
    {
        return database.Query(
                "SELECT column_name, referenced_table_name, referenced_column_name FROM foreign_keys WHERE table_name = $name COLLATE NOCASE ORDER BY constraint_name, ordinal",
                ("$name", objectName))
            .Select(row => new IndexedForeignKey(
                TextOrDefault(row, "column_name", ""),
                TextOrDefault(row, "referenced_table_name", ""),
                TextOrDefault(row, "referenced_column_name", "")))
            .ToArray();
    }

    /// <summary>Returns whether one indexed object is a procedure.</summary>
    public bool HasProcedure(string objectName)
    {
        string qualifiedName = database.QualifyObjectName(objectName);
        Dictionary<string, object?>? row = database.Query(
                "SELECT 1 FROM objects WHERE name = $name COLLATE NOCASE AND kind = 'procedure' LIMIT 1",
                ("$name", qualifiedName))
            .SingleOrDefault();

        return row is not null;
    }

    /// <summary>Returns parameters for one indexed procedure in declaration order.</summary>
    public IReadOnlyList<ObjectParameter> ParametersForProcedure(string objectName)
    {
        string qualifiedName = database.QualifyObjectName(objectName);
        Dictionary<string, object?>? objectRow = database.Query(
                "SELECT name FROM objects WHERE name = $name COLLATE NOCASE AND kind = 'procedure' ORDER BY name LIMIT 1",
                ("$name", qualifiedName))
            .SingleOrDefault();

        if (objectRow is null)
        {
            return [];
        }

        string storedName = TextOrDefault(objectRow, "name", qualifiedName);

        return database.Query(
                "SELECT object_name, ordinal, name, data_type, default_value, is_output, source_file FROM parameters WHERE object_name = $name COLLATE NOCASE ORDER BY ordinal",
                ("$name", storedName))
            .Select(row => new ObjectParameter(
                TextOrDefault(row, "object_name", storedName),
                Convert.ToInt32(row["ordinal"]),
                TextOrDefault(row, "name", ""),
                TextOrDefault(row, "data_type", ""),
                row["default_value"]?.ToString(),
                Convert.ToInt32(row["is_output"]) != 0,
                TextOrDefault(row, "source_file", "")))
            .ToArray();
    }

    /// <summary>Returns the combined indexed lint, parse, dynamic-SQL, and reachability findings.</summary>
    /// <param name="limit">An optional maximum row count.</param>
    public List<Dictionary<string, object?>> Lint(int? limit = null)
    {
        return database.Query(WithLimit(LintQuery, limit), ("$limit", limit));
    }

    private static string WithLimit(string sql, int? limit) => limit is null ? sql : sql + " LIMIT $limit";

    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    private static string TextOrDefault(
        IReadOnlyDictionary<string, object?> row,
        string key,
        string fallback)
    {
        if (!row.TryGetValue(key, out object? value) || value is null)
        {
            return fallback;
        }

        return value.ToString()!;
    }
}
