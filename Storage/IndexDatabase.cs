using Microsoft.Data.Sqlite;
using SqlDeps.Indexing;

namespace SqlDeps.Storage;

/// <summary>Read-only access to an sqldeps SQLite index and its freshness metadata.</summary>
public sealed class IndexDatabase(string path)
{
    public string Path => path;

    public string? SourceRoot => MetaValue("source_root");
    public string DefaultSchema => MetaValue("default_schema") ?? "dbo";
    public List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] parameters)
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
            Dictionary<string, object?> row = [];

            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Qualifies a one-part CLI name with the schema recorded by the index.</summary>
    public string QualifyObjectName(string name)
    {
        return name.Contains('.') ? name : DefaultSchema + "." + name;
    }

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
        return SqlFolderIndexer.FindSqlFiles(folder)
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
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)command.ExecuteScalar()!;
    }

    private string? MetaValue(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }
}

public sealed record IndexStatus(int SqlFiles, int IndexedFiles, int Added, int Changed, int Removed, long Objects, long Dependencies, long ParseErrors)
{
    public bool IsCurrent => Added == 0 && Changed == 0 && Removed == 0;
}

public sealed record IndexDrift(string Path, string State);
