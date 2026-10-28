using Microsoft.Data.Sqlite;

namespace SqlDeps;

/// <summary>What one file contributed to the previous index, so an unchanged file need not be parsed again.</summary>
public sealed record CachedFile(
    string Hash,
    List<DatabaseObject> Objects,
    List<Dependency> Dependencies,
    List<ParseProblem> Problems
);

/// <summary>
/// Reads the previous index back. The cache is only trusted when it was built with the same settings
/// (the default schema) and the same version of the indexing rules; otherwise everything is read again.
/// </summary>
public static class IndexCache
{
    /// <summary>Bump when the rules that turn a script into dependencies change, so old caches are dropped.</summary>
    public const int RulesVersion = 1;

    public static string SettingsFor(string defaultSchema)
    {
        return "rules=" + RulesVersion + ";default_schema=" + defaultSchema;
    }

    public static Dictionary<string, CachedFile> Load(
        string databasePath,
        string settings
    )
    {
        var cache = new Dictionary<string, CachedFile>(StringComparer.Ordinal);

        if (!File.Exists(databasePath))
        {
            return cache;
        }

        try
        {
            using var connection = new SqliteConnection("Data Source=" + databasePath + ";Mode=ReadOnly");
            connection.Open();

            if (ReadSettings(connection) != settings)
            {
                return cache;
            }

            LoadFiles(connection, cache);
            LoadObjects(connection, cache);
            LoadDependencies(connection, cache);
            LoadProblems(connection, cache);

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
            var emptyFile = new CachedFile(
                reader.GetString(1),
                new List<DatabaseObject>(),
                new List<Dependency>(),
                new List<ParseProblem>()
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
        command.CommandText = "SELECT source_object, target_object, operation, source_file, line FROM dependencies ORDER BY rowid";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var file = reader.GetString(3);

            if (cache.TryGetValue(file, out var cached))
            {
                var dependency = new Dependency(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    file,
                    reader.GetInt32(4)
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
}
