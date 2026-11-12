using Microsoft.Data.Sqlite;
using SqlDeps.Storage;

namespace SqlDeps;

public static partial class IndexCache
{
    private static (
        Dictionary<string, CachedFile> Files,
        Dictionary<string, List<CachedBatch>> Batches) LoadData(
        string databasePath,
        string settings,
        bool loadFiles,
        bool loadBatches)
    {
        Dictionary<string, List<CachedBatch>> batchCache = new(StringComparer.Ordinal);
        Dictionary<string, CachedFile> files = new(StringComparer.Ordinal);
        using SqliteConnection? connection = OpenCompatibleConnection(databasePath, settings);

        if (connection is null)
        {

            return (files, batchCache);
        }

        try
        {
            if (loadFiles)
            {
                LoadCache(connection, files);
            }

            if (loadBatches)
            {
                LoadBatchCache(connection, batchCache);
            }
        }
        catch (SqliteException)
        {

            return (
                new Dictionary<string, CachedFile>(StringComparer.Ordinal),
                new Dictionary<string, List<CachedBatch>>(StringComparer.Ordinal));
        }

        return (files, batchCache);
    }

    private static void LoadBatchCache(
        SqliteConnection connection,
        Dictionary<string, List<CachedBatch>> cache)
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

    private static string ReadSettings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'settings'";
        var value = command.ExecuteScalar();

        return value is string text ? text : "";
    }
}
