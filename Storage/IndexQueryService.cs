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
            .Load(
                database.Path,
                IndexCache.SettingsFor(
                    database.DefaultSchema,
                    database.SourceRoot,
                    database.DefaultDatabase,
                    database.IncludePatterns,
                    database.ExcludePatterns))
            .Values;

        return new IndexAnalysisMetadata(
            new MetadataCatalog(
                files.SelectMany(file => file.ResultColumns),
                files.SelectMany(file => file.TableColumns),
                files.SelectMany(file => file.TableKeys),
                files.SelectMany(file => file.ForeignKeys),
                files.SelectMany(file => file.IndexColumns)),
            files.SelectMany(file => file.Objects).ToArray(),
            files.SelectMany(file => file.Parameters).ToArray(),
            database.DefaultDatabase);
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

    /// <summary>Returns direct or transitive callers of one indexed object with edge evidence.</summary>
    public List<Dictionary<string, object?>> Callers(
        string objectName,
        bool recursive,
        int maxDepth,
        int? limit = null)
    {
        if (maxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Caller depth must be positive.");
        }

        string qualifiedName = database.QualifyObjectName(objectName);
        var dependencies = database.Query(
            "SELECT source_object, target_object, operation, source_file, line, via_dynamic FROM dependencies WHERE source_object <> '(batch)'");
        var edgeRows = dependencies
            .GroupBy(row => EdgeKey(TextOrDefault(row, "source_object", ""), TextOrDefault(row, "operation", ""), TextOrDefault(row, "target_object", "")), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        List<Dictionary<string, object?>> rows = [];

        if (!recursive)
        {
            foreach (var dependency in dependencies.Where(row => SameText(row, "target_object", qualifiedName)))
            {
                rows.Add(CallerRow(
                    qualifiedName,
                    TextOrDefault(dependency, "source_object", ""),
                    TextOrDefault(dependency, "target_object", ""),
                    TextOrDefault(dependency, "operation", ""),
                    TextOrDefault(dependency, "source_file", ""),
                    dependency["line"],
                    dependency["via_dynamic"],
                    1,
                    BuildPath(
                        TextOrDefault(dependency, "source_object", ""),
                        TextOrDefault(dependency, "operation", ""),
                        TextOrDefault(dependency, "target_object", ""))));
            }
        }
        else
        {
            var paths = database.Query(
                "SELECT source_object, reached_object, depth, path FROM reach WHERE reached_object = $name COLLATE NOCASE AND depth <= $depth ORDER BY depth, source_object, path",
                ("$name", qualifiedName),
                ("$depth", maxDepth));

            foreach (var pathRow in paths)
            {
                string root = TextOrDefault(pathRow, "source_object", "");
                string path = TextOrDefault(pathRow, "path", "");
                var pathEdges = ParsePath(path).ToArray();

                for (var index = 0; index < pathEdges.Length; index++)
                {
                    var edge = pathEdges[index];
                    var matchingRows = edgeRows.GetValueOrDefault(EdgeKey(edge.Source, edge.Operation, edge.Target));

                    if (matchingRows is null || matchingRows.Length == 0)
                    {
                        rows.Add(CallerRow(root, edge.Source, edge.Target, edge.Operation, "", null, null, index + 1, path));

                        continue;
                    }

                    foreach (var dependency in matchingRows)
                    {
                        rows.Add(CallerRow(
                            root,
                            edge.Source,
                            edge.Target,
                            edge.Operation,
                            TextOrDefault(dependency, "source_file", ""),
                            dependency["line"],
                            dependency["via_dynamic"],
                            index + 1,
                            path));
                    }
                }
            }

            AppendCallerCycleRows(rows, qualifiedName, maxDepth);
            AppendCallerTruncationRows(rows, qualifiedName, maxDepth, dependencies);
        }

        return rows
            .OrderBy(row => Convert.ToInt32(row["depth"]))
            .ThenBy(row => TextOrDefault(row, "root", ""), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => TextOrDefault(row, "source", ""), StringComparer.OrdinalIgnoreCase)
            .Take(limit ?? int.MaxValue)
            .ToList();
    }

    /// <summary>Returns literal object candidates from unresolved dynamic SQL without treating them as graph edges.</summary>
    public List<Dictionary<string, object?>> DynamicSqlCandidates(
        string? sourceObject = null,
        int? limit = null)
    {
        const string query = """
            SELECT source_object, candidate_object, operation, confidence, evidence, source_file, line
            FROM dynamic_sql_candidates
            WHERE ($source IS NULL OR source_object = $source COLLATE NOCASE)
            ORDER BY source_object, candidate_object, operation, source_file, line
            """;

        string? qualifiedSource = string.IsNullOrWhiteSpace(sourceObject)
            ? null
            : database.QualifyObjectName(sourceObject);

        return database.Query(
            WithLimit(query, limit),
            ("$source", qualifiedSource),
            ("$limit", limit));
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
        string qualifiedName = database.QualifyObjectName(objectName);

        return database.Query(
                "SELECT column_name FROM table_columns WHERE table_name = $name COLLATE NOCASE ORDER BY rowid",
                ("$name", qualifiedName))
            .Select(row => TextOrDefault(row, "column_name", ""))
            .ToArray();
    }

    /// <summary>Returns indexed foreign-key relations for one table or view.</summary>
    public IReadOnlyList<IndexedForeignKey> ForeignKeysFor(string objectName)
    {
        string qualifiedName = database.QualifyObjectName(objectName);

        return database.Query(
                "SELECT column_name, referenced_table_name, referenced_column_name FROM foreign_keys WHERE table_name = $name COLLATE NOCASE ORDER BY constraint_name, ordinal",
                ("$name", qualifiedName))
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

    private static Dictionary<string, object?> CallerRow(
        string root,
        string source,
        string target,
        string operation,
        string sourceFile,
        object? line,
        object? viaDynamic,
        int depth,
        string path,
        string kind = "edge",
        bool cycle = false,
        bool truncated = false)
    {
        return new Dictionary<string, object?>
        {
            ["root"] = root,
            ["source"] = source,
            ["target"] = target,
            ["operation"] = operation,
            ["source_file"] = sourceFile,
            ["line"] = line,
            ["via_dynamic"] = viaDynamic,
            ["depth"] = depth,
            ["path"] = path,
            ["kind"] = kind,
            ["cycle"] = cycle,
            ["truncated"] = truncated,
        };
    }

    private void AppendCallerCycleRows(
        ICollection<Dictionary<string, object?>> rows,
        string qualifiedName,
        int maxDepth)
    {
        var cycles = database.Query(
            """
            SELECT object_name, depth, path
            FROM cycles
            WHERE object_name = $name COLLATE NOCASE
               OR object_name IN (
                    SELECT source_object
                    FROM reach
                    WHERE reached_object = $name COLLATE NOCASE AND depth <= $depth
               )
            ORDER BY depth, object_name, path
            """,
            ("$name", qualifiedName),
            ("$depth", maxDepth));

        foreach (var cycle in cycles)
        {
            string objectName = TextOrDefault(cycle, "object_name", qualifiedName);
            int depth = Convert.ToInt32(cycle["depth"]);
            string path = TextOrDefault(cycle, "path", objectName);

            rows.Add(CallerRow(
                objectName,
                objectName,
                qualifiedName,
                "CYCLE",
                "",
                null,
                null,
                depth,
                path,
                kind: "cycle",
                cycle: true));
        }
    }

    private static void AppendCallerTruncationRows(
        ICollection<Dictionary<string, object?>> rows,
        string qualifiedName,
        int maxDepth,
        IReadOnlyList<Dictionary<string, object?>> dependencies)
    {
        var candidates = rows
            .Where(row => row.TryGetValue("kind", out var kind) && string.Equals(kind?.ToString(), "edge", StringComparison.Ordinal))
            .Where(row => Convert.ToInt32(row["depth"]) == maxDepth)
            .ToArray();
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            string root = TextOrDefault(candidate, "root", "");
            string path = TextOrDefault(candidate, "path", "");
            HashSet<string> pathNodes = PathNodes(path);
            bool hasUnrepresentedCaller = dependencies.Any(dependency =>
                !SameText(dependency, "source_object", "(batch)")
                && !SameText(dependency, "target_object", ObjectNames.DynamicSql)
                && SameText(dependency, "target_object", root)
                && !pathNodes.Contains(TextOrDefault(dependency, "source_object", "")));

            if (!hasUnrepresentedCaller || !reported.Add(root))
            {
                continue;
            }

            rows.Add(CallerRow(
                root,
                root,
                qualifiedName,
                "TRUNCATED",
                "",
                null,
                null,
                maxDepth,
                path,
                kind: "truncation",
                truncated: true));
        }
    }

    private static HashSet<string> PathNodes(string path)
    {
        HashSet<string> nodes = new(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in ParsePath(path))
        {
            nodes.Add(edge.Source);
            nodes.Add(edge.Target);
        }

        return nodes;
    }

    private static IEnumerable<(string Source, string Operation, string Target)> ParsePath(string path)
    {
        var remaining = path;

        while (true)
        {
            var separator = remaining.IndexOf(" -", StringComparison.Ordinal);
            if (separator < 0)
            {
                yield break;
            }

            var operationEnd = remaining.IndexOf("-> ", separator + 2, StringComparison.Ordinal);
            if (operationEnd < 0)
            {
                yield break;
            }

            var source = remaining[..separator];
            var operation = remaining[(separator + 2)..operationEnd];
            var targetStart = operationEnd + 3;
            var nextSeparator = remaining.IndexOf(" -", targetStart, StringComparison.Ordinal);
            var target = nextSeparator < 0
                ? remaining[targetStart..]
                : remaining[targetStart..nextSeparator];

            yield return (source, operation, target);

            if (nextSeparator < 0)
            {
                yield break;
            }

            remaining = remaining[targetStart..];
        }
    }

    private static string BuildPath(string source, string operation, string target)
    {
        return $"{source} -{operation}-> {target}";
    }

    private static string EdgeKey(string source, string operation, string target)
    {
        return source + "\u001f" + operation + "\u001f" + target;
    }

    private static bool SameText(IReadOnlyDictionary<string, object?> row, string key, string value)
    {
        return string.Equals(TextOrDefault(row, key, ""), value, StringComparison.OrdinalIgnoreCase);
    }

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
