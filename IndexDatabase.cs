using Microsoft.Data.Sqlite;

namespace SqlDeps;

/// <summary>Read-only access to an sqldeps SQLite index and its freshness metadata.</summary>
public sealed class IndexDatabase(string path)
{
    public List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }

    public IndexStatus GetStatus(string folder)
    {
        var indexed = Query("SELECT path, sha256 FROM files")
            .ToDictionary(row => (string)row["path"]!, row => (string)row["sha256"]!, StringComparer.Ordinal);
        var actual = SqlFolderIndexer.FindSqlFiles(folder)
            .ToDictionary(path => Path.GetRelativePath(folder, path), path => SqlFileReader.HashOf(File.ReadAllBytes(path)), StringComparer.Ordinal);

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

    private SqliteConnection Open()
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("No index database: " + path);
        }

        var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly");
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
}

public sealed record IndexStatus(int SqlFiles, int IndexedFiles, int Added, int Changed, int Removed, long Objects, long Dependencies, long ParseErrors)
{
    public bool IsCurrent => Added == 0 && Changed == 0 && Removed == 0;
}
