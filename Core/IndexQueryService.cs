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

    public IndexAnalysisMetadata LoadAnalysisMetadata()
    {
        IReadOnlyCollection<CachedFile> files = IndexCache
            .Load(database.Path, IndexCache.SettingsFor(database.DefaultSchema))
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

    public QueryPreview BuildSelectPreview(string objectName, bool includeJoin = false)
    {
        var qualifiedName = database.QualifyObjectName(objectName);
        var objectRow = database.Query(
            "SELECT name, kind FROM objects WHERE name = $name COLLATE NOCASE AND kind IN ('table', 'view') ORDER BY kind, name LIMIT 1",
            ("$name", qualifiedName)
        ).SingleOrDefault();

        if (objectRow is null)
        {
            throw new ArgumentException($"No indexed table or view: {qualifiedName}", nameof(objectName));
        }

        var columns = ColumnsFor(qualifiedName);

        if (!includeJoin)
        {
            return BuildSingleTablePreview(qualifiedName, objectRow, columns);
        }

        return BuildJoinPreview(qualifiedName, objectRow, columns, objectName);
    }

    private static QueryPreview BuildSingleTablePreview(
        string qualifiedName,
        IReadOnlyDictionary<string, object?> objectRow,
        IEnumerable<string> columns)
    {
        return SqlQueryBuilder.BuildSelect(
            objectRow["name"]?.ToString() ?? qualifiedName,
            objectRow["kind"]?.ToString() ?? "table",
            columns);
    }

    private QueryPreview BuildJoinPreview(
        string qualifiedName,
        IReadOnlyDictionary<string, object?> objectRow,
        IEnumerable<string> columns,
        string objectName)
    {
        var relation = database.Query(
                "SELECT column_name, referenced_table_name, referenced_column_name FROM foreign_keys WHERE table_name = $name COLLATE NOCASE ORDER BY constraint_name, ordinal",
                ("$name", qualifiedName))
            .ToArray();

        if (relation.Length != 1)
        {
            throw new ArgumentException($"JOIN preview requires exactly one single-column foreign key for {qualifiedName}.", nameof(objectName));
        }

        var targetName = relation[0]["referenced_table_name"]?.ToString();
        var targetKey = relation[0]["referenced_column_name"]?.ToString();
        var sourceKey = relation[0]["column_name"]?.ToString();

        if (HasIncompleteJoinMetadata(targetName, targetKey, sourceKey))
        {
            throw new ArgumentException($"JOIN metadata is incomplete for {qualifiedName}.", nameof(objectName));
        }

        var target = database.Query(
                "SELECT name, kind FROM objects WHERE name = $name COLLATE NOCASE AND kind IN ('table', 'view') ORDER BY kind, name LIMIT 1",
                ("$name", targetName))
            .SingleOrDefault();

        if (target is null)
        {
            throw new ArgumentException($"Referenced table is not indexed: {targetName}", nameof(objectName));
        }

        var targetObjectName = target["name"]?.ToString() ?? targetName!;

        return SqlQueryBuilder.BuildSelectWithJoin(
            objectRow["name"]?.ToString() ?? qualifiedName,
            objectRow["kind"]?.ToString() ?? "table",
            columns,
            targetObjectName,
            ColumnsFor(targetObjectName),
            sourceKey!,
            targetKey!);
    }

    private static bool HasIncompleteJoinMetadata(string? targetName, string? targetKey, string? sourceKey)
    {
        return string.IsNullOrWhiteSpace(targetName)
            || string.IsNullOrWhiteSpace(targetKey)
            || string.IsNullOrWhiteSpace(sourceKey);
    }

    private IEnumerable<string> ColumnsFor(string objectName)
    {
        return database.Query(
                "SELECT column_name FROM table_columns WHERE table_name = $name COLLATE NOCASE ORDER BY rowid",
                ("$name", objectName))
            .Select(row => row["column_name"]?.ToString() ?? "");
    }

    public SqlTestSkeleton BuildTestSkeleton(string objectName)
    {
        var qualifiedName = database.QualifyObjectName(objectName);
        var objectRow = database.Query(
                "SELECT name, kind FROM objects WHERE name = $name COLLATE NOCASE AND kind = 'procedure' ORDER BY name LIMIT 1",
                ("$name", qualifiedName))
            .SingleOrDefault();


        if (objectRow is null)
        {

            throw new ArgumentException($"No indexed procedure: {qualifiedName}", nameof(objectName));
        }

        var parameters = database.Query(
                "SELECT object_name, ordinal, name, data_type, default_value, is_output, source_file FROM parameters WHERE object_name = $name COLLATE NOCASE ORDER BY ordinal",
                ("$name", qualifiedName))
            .Select(row => new ObjectParameter(
                row["object_name"]?.ToString() ?? qualifiedName,
                Convert.ToInt32(row["ordinal"]),
                row["name"]?.ToString() ?? "",
                row["data_type"]?.ToString() ?? "",
                row["default_value"]?.ToString(),
                Convert.ToInt32(row["is_output"]) != 0,
                row["source_file"]?.ToString() ?? ""));


        return SqlTestSkeletonBuilder.Build(objectRow["name"]?.ToString() ?? qualifiedName, parameters);
    }

    public List<Dictionary<string, object?>> Lint(int? limit = null)
    {
        return database.Query(WithLimit(LintQuery, limit), ("$limit", limit));
    }

    private static string WithLimit(string sql, int? limit) => limit is null ? sql : sql + " LIMIT $limit";

    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
}
