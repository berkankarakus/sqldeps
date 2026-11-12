using Microsoft.Data.Sqlite;

namespace SqlDeps.Storage;

/// <summary>Read-only access to an sqldeps SQLite index and its freshness metadata.</summary>
public sealed class IndexDatabase(string path)
{
    /// <summary>The SQLite index path.</summary>
    public string Path => path;

    /// <summary>The indexed source root, when stored in metadata.</summary>
    public string? SourceRoot => MetaValue("source_root");

    /// <summary>The default schema used for one-part object names.</summary>
    public string DefaultSchema => MetaValue("default_schema") ?? "dbo";

    /// <summary>Executes a read-only parameterized query and returns rows by column name.</summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">The named SQLite parameters.</param>
    /// <returns>The query result rows.</returns>
    internal List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (Name, Value) in parameters)
        {
            command.Parameters.AddWithValue(Name, Value);
        }

        using var reader = command.ExecuteReader();
        List<Dictionary<string, object?>> rows = [];

        while (reader.Read())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    private static Dictionary<string, object?> ReadRow(SqliteDataReader reader)
    {
        Dictionary<string, object?> row = [];

        for (var column = 0; column < reader.FieldCount; column++)
        {
            row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
        }

        return row;
    }

    /// <summary>Qualifies a one-part CLI name with the schema recorded by the index.</summary>
    public string QualifyObjectName(string name)
    {
        return name.Contains('.') ? name : DefaultSchema + "." + name;
    }

    /// <summary>Compares indexed file hashes with the current SQL files in a folder.</summary>
    public IndexStatus GetStatus(string folder)
    {
        var indexed = LoadIndexedHashes();
        var actual = LoadActualHashes(folder);

        return new IndexStatus(
            actual.Count,
            indexed.Count,
            actual.Keys.Count(path => !indexed.ContainsKey(path)),
            actual.Count(pair => indexed.TryGetValue(pair.Key, out var hash) && hash != pair.Value),
            indexed.Keys.Count(path => !actual.ContainsKey(path)),
            Scalar("SELECT COUNT(*) FROM objects"),
            Scalar("SELECT COUNT(*) FROM dependencies"),
            Scalar("SELECT COUNT(*) FROM parse_errors")
        );
    }

    /// <summary>Lists added, changed, and removed SQL files since indexing.</summary>
    public IReadOnlyList<IndexDrift> GetDrift(string folder)
    {
        var indexed = LoadIndexedHashes();
        var actual = LoadActualHashes(folder);
        var drift = actual
            .Where(pair => !indexed.TryGetValue(pair.Key, out var hash)
                || !string.Equals(hash, pair.Value, StringComparison.Ordinal))
            .Select(pair => new IndexDrift(
                pair.Key,
                indexed.ContainsKey(pair.Key) ? "changed" : "added"))
            .Concat(indexed.Keys
                .Where(path => !actual.ContainsKey(path))
                .Select(path => new IndexDrift(path, "removed")))
            .OrderBy(item => item.State)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

        return drift;
    }

    private Dictionary<string, string> LoadIndexedHashes()
    {
        return Query("SELECT path, sha256 FROM files")
            .ToDictionary(
                row => (string)row["path"]!,
                row => (string)row["sha256"]!,
                StringComparer.Ordinal
            );
    }

    private static Dictionary<string, string> LoadActualHashes(string folder)
    {
        return SqlFileCatalog.FindSqlFiles(folder)
            .ToDictionary(
                path => System.IO.Path.GetRelativePath(folder, path),
                path => SqlFileReader.HashOf(File.ReadAllBytes(path)),
                StringComparer.Ordinal
            );
    }

    private SqliteConnection Open()
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("No index database: " + path);
        }

        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        };
        SqliteConnection connection = new(connectionString.ToString());
        connection.Open();

        return connection;
    }

    private long Scalar(string sql)
    {
        return (long)ExecuteScalar(sql)!;
    }

    private string? MetaValue(string key)
    {
        return ExecuteScalar(
            "SELECT value FROM meta WHERE key = $key",
            ("$key", key)) as string;
    }

    private object? ExecuteScalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar();
    }
}

/// <summary>Freshness and content counts for an index.</summary>
/// <param name="SqlFiles">The current SQL file count.</param>
/// <param name="IndexedFiles">The stored SQL file count.</param>
/// <param name="Added">The number of current files missing from the index.</param>
/// <param name="Changed">The number of current files with a different hash.</param>
/// <param name="Removed">The number of indexed files no longer present.</param>
/// <param name="Objects">The indexed object count.</param>
/// <param name="Dependencies">The indexed dependency count.</param>
/// <param name="ParseErrors">The indexed parse-error count.</param>
public sealed record IndexStatus(int SqlFiles, int IndexedFiles, int Added, int Changed, int Removed, long Objects, long Dependencies, long ParseErrors)
{
    /// <summary>Whether indexed and current SQL file sets match.</summary>
    public bool IsCurrent => Added == 0 && Changed == 0 && Removed == 0;
}

/// <summary>One file difference between the index and its source folder.</summary>
/// <param name="Path">The relative SQL file path.</param>
/// <param name="State">The drift state: added, changed, or removed.</param>
public sealed record IndexDrift(string Path, string State);
