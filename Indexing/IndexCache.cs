using Microsoft.Data.Sqlite;

namespace SqlDeps;

/// <summary>What one file contributed to the previous index, so an unchanged file need not be parsed again.</summary>
public sealed record CachedFile(
    string Hash,
    List<DatabaseObject> Objects,
    List<Dependency> Dependencies,
    List<ObjectParameter> Parameters,
    List<ColumnUsage> ColumnUsage,
    List<TableColumn> TableColumns,
    List<ParseProblem> Problems,
    List<IndexWarning> Warnings,
    List<LintFinding> LintFindings
);

/// <summary>
/// Reads the previous index back. The cache is only trusted when it was built with the same settings
/// (the default schema) and the same version of the indexing rules; otherwise everything is read again.
/// </summary>
public static class IndexCache
{
    /// <summary>Bump when the rules that turn a script into dependencies change, so old caches are dropped.</summary>
    public const int RulesVersion = 10;

    public static string SettingsFor(string defaultSchema)
    {
        return $"rules={RulesVersion};default_schema={defaultSchema}";
    }

    public static Dictionary<string, CachedFile> Load(
        string databasePath,
        string settings
    )
    {
        Dictionary<string, CachedFile> cache = new(StringComparer.Ordinal);

        if (!File.Exists(databasePath))
        {
            return cache;
        }

        try
        {
            using SqliteConnection connection = new("Data Source=" + databasePath + ";Mode=ReadOnly");
            connection.Open();

            if (ReadSettings(connection) != settings)
            {
                return cache;
            }

            LoadFiles(connection, cache);
            LoadObjects(connection, cache);
            LoadDependencies(connection, cache);
            LoadParameters(connection, cache);
            LoadColumnUsage(connection, cache);
            LoadTableColumns(connection, cache);
            LoadProblems(connection, cache);
            LoadWarnings(connection, cache);
            LoadLintFindings(connection, cache);

            return cache;
        }

        catch (SqliteException)
        {
            // An index from an older version of the tool, or a file that is not an index: start over.
            return new Dictionary<string, CachedFile>(StringComparer.Ordinal);
        }
    }

    private static string ReadSettings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'settings'";
        var value = command.ExecuteScalar();

        return value is string text ? text : "";
    }

    private static void LoadFiles(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, sha256 FROM files";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            CachedFile emptyFile = new(
                reader.GetString(1),
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []
            );

            cache[reader.GetString(0)] = emptyFile;
        }
    }

    private static void LoadObjects(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, kind, source_file, line FROM objects ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(2);

            if (cache.TryGetValue(file, out var cached))
            {
                cached.Objects.Add(new DatabaseObject(reader.GetString(0), reader.GetString(1), file, reader.GetInt32(3)));
            }
        }
    }

    private static void LoadDependencies(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_object, target_object, operation, source_file, line, via_dynamic FROM dependencies ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(3);

            if (cache.TryGetValue(file, out var cached))
            {
                Dependency dependency = new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    file,
                    reader.GetInt32(4),
                    reader.GetInt32(5) != 0
                );

                cached.Dependencies.Add(dependency);
            }
        }
    }

    private static void LoadProblems(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_file, line, message FROM parse_errors ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(0);

            if (cache.TryGetValue(file, out var cached))
            {
                cached.Problems.Add(new ParseProblem(file, reader.GetInt32(1), reader.GetString(2)));
            }
        }
    }

    private static void LoadParameters(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_name, ordinal, name, data_type, default_value, is_output, source_file FROM parameters ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(6);

            if (cache.TryGetValue(file, out var cached))
            {
                cached.Parameters.Add(new ObjectParameter(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5) != 0,
                    file
                ));
            }
        }
    }

    private static void LoadColumnUsage(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_object, table_name, column_name, operation, source_file, line FROM column_usage ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(4);

            if (cache.TryGetValue(file, out var cached))
            {
                cached.ColumnUsage.Add(new ColumnUsage(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    file,
                    reader.GetInt32(5)
                ));
            }
        }
    }

    private static void LoadWarnings(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_file, line, message FROM parse_warnings ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(0);

            if (cache.TryGetValue(file, out var cached))
            {
                cached.Warnings.Add(new IndexWarning(file, reader.GetInt32(1), reader.GetString(2)));
            }
        }
    }

    private static void LoadTableColumns(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name, column_name, data_type, source_file, line FROM table_columns ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(3);
            if (cache.TryGetValue(file, out var cached))
            {
                cached.TableColumns.Add(new TableColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2), file, reader.GetInt32(4)));
            }
        }
    }

    private static void LoadLintFindings(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rule, severity, source_object, source_file, line, column, length, message FROM lint_findings ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(3);
            if (cache.TryGetValue(file, out var cached))
            {
                cached.LintFindings.Add(new LintFinding(reader.GetString(0), reader.GetString(1), reader.GetString(2), file, reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7)));
            }
        }
    }
}
