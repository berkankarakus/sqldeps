using Microsoft.Data.Sqlite;

namespace SqlDeps;

/// <summary>What one file contributed to the previous index, so an unchanged file need not be parsed again.</summary>
public sealed record CachedFile(
    string Hash,
    List<DatabaseObject> Objects,
    List<Dependency> Dependencies,
    List<ObjectParameter> Parameters,
    List<ResultColumn> ResultColumns,
    List<TableKey> TableKeys,
    List<TableForeignKey> ForeignKeys,
    List<TableIndexColumn> IndexColumns,
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
    public const int RulesVersion = 20;

    /// <summary>Builds the cache compatibility key for parser rules and name-resolution settings.</summary>
    public static string SettingsFor(string defaultSchema)
    {
        return $"rules={RulesVersion};default_schema={defaultSchema}";
    }

    /// <summary>Loads per-file contributions when the database schema and settings are compatible.</summary>
    public static Dictionary<string, CachedFile> Load(
        string databasePath,
        string settings
    )
    {
        Dictionary<string, CachedFile> cache = new(StringComparer.Ordinal);

        using SqliteConnection? connection = OpenCompatibleConnection(databasePath, settings);

        if (connection is null)
        {

            return cache;
        }

        try
        {
            LoadCache(connection, cache);
        }
        catch (SqliteException)
        {

            return new Dictionary<string, CachedFile>(StringComparer.Ordinal);
        }


        return cache;
    }

    /// <summary>Loads reusable batch contributions when the database is compatible.</summary>
    public static Dictionary<string, List<CachedBatch>> LoadBatches(
        string databasePath,
        string settings)
    {
        Dictionary<string, List<CachedBatch>> cache = new(StringComparer.Ordinal);
        using SqliteConnection? connection = OpenCompatibleConnection(databasePath, settings);

        if (connection is null)
        {

            return cache;
        }

        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT source_file, ordinal, start_line, text_hash, payload FROM file_batches ORDER BY source_file, ordinal";
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                CachedBatch batch = new(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4));

                if (!cache.TryGetValue(batch.SourceFile, out List<CachedBatch>? fileBatches))
                {
                    fileBatches = [];
                    cache.Add(batch.SourceFile, fileBatches);
                }

                fileBatches.Add(batch);
            }
        }
        catch (SqliteException)
        {

            return new Dictionary<string, List<CachedBatch>>(StringComparer.Ordinal);
        }


        return cache;
    }

    private static SqliteConnection? OpenCompatibleConnection(string databasePath, string settings)
    {
        if (!File.Exists(databasePath))
        {

            return null;
        }

        SqliteConnection? connection = null;

        try
        {
            SqliteConnectionStringBuilder connectionString = new()
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            connection = new SqliteConnection(connectionString.ToString());
            connection.Open();

            if (!IsCompatible(connection, settings))
            {
                connection.Dispose();


                return null;
            }


            return connection;
        }
        catch (SqliteException)
        {
            connection?.Dispose();


            return null;
        }
    }

    private static bool IsCompatible(SqliteConnection connection, string settings)
    {
        return SqliteSchema.IsCompatible(connection)
            && ReadSettings(connection) == settings;
    }

    private static void LoadCache(
        SqliteConnection connection,
        Dictionary<string, CachedFile> cache)
    {
        LoadFiles(connection, cache);
        LoadObjects(connection, cache);
        LoadDependencies(connection, cache);
        LoadParameters(connection, cache);
        LoadResultColumns(connection, cache);
        LoadTableKeys(connection, cache);
        LoadForeignKeys(connection, cache);
        LoadIndexColumns(connection, cache);
        LoadColumnUsage(connection, cache);
        LoadTableColumns(connection, cache);
        LoadProblems(connection, cache);
        LoadWarnings(connection, cache);
        LoadLintFindings(connection, cache);
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
            var path = reader.GetString(0);
            var hash = reader.GetString(1);

            cache[path] = EmptyFile(hash);
        }
    }

    private static CachedFile EmptyFile(string hash) => new(
        hash,
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        []
    );

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

    private static void LoadResultColumns(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_name, ordinal, column_name, source_file, line, is_known_name, data_type FROM result_columns ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = SqliteMetadataMapper.ReadResultColumn(reader);
            if (cache.TryGetValue(row.SourceFile, out var cached))
            {
                cached.ResultColumns.Add(row.ToModel());
            }
        }
    }

    private static void LoadTableKeys(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name, kind, constraint_name, column_name, ordinal, source_file, line FROM table_keys ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(5);
            if (cache.TryGetValue(file, out var cached))
            {
                cached.TableKeys.Add(new TableKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), file, reader.GetInt32(6)));
            }
        }
    }

    private static void LoadForeignKeys(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name, constraint_name, column_name, referenced_table_name, referenced_column_name, ordinal, source_file, line FROM foreign_keys ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(6);
            if (cache.TryGetValue(file, out var cached))
            {
                cached.ForeignKeys.Add(new TableForeignKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), file, reader.GetInt32(7)));
            }
        }
    }

    private static void LoadIndexColumns(SqliteConnection connection, Dictionary<string, CachedFile> cache)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name, index_name, column_name, ordinal, is_included, is_descending, is_unique, is_clustered, is_filtered, filter_predicate, source_file, line FROM index_columns ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = SqliteMetadataMapper.ReadTableIndexColumn(reader);
            if (cache.TryGetValue(row.SourceFile, out var cached))
            {
                cached.IndexColumns.Add(row.ToModel());
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
        command.CommandText = "SELECT table_name, column_name, data_type, is_nullable, is_identity, default_value, is_computed, precision, scale, collation, source_file, line FROM table_columns ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = SqliteMetadataMapper.ReadTableColumn(reader);
            if (cache.TryGetValue(row.SourceFile, out var cached))
            {
                cached.TableColumns.Add(row.ToModel());
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
