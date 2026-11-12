using Microsoft.Data.Sqlite;

namespace SqlDeps.Storage;

/// <summary>Caches prepared insert commands for one SQLite transaction.</summary>
internal sealed class SqliteBatchInserter(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
{
    private static readonly string[] ParameterNames = ["$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i", "$j", "$k", "$l"];
    private readonly Dictionary<string, SqliteCommand> commands = new(StringComparer.Ordinal);

    internal void Insert(string sql, params object?[] values)
    {
        if (!commands.TryGetValue(sql, out var command))
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            for (var index = 0; index < ParameterNames.Length; index++)
            {
                command.Parameters.Add(new SqliteParameter(ParameterNames[index], DBNull.Value));
            }

            command.Prepare();
            commands.Add(sql, command);
        }

        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters[index].Value = values[index] ?? DBNull.Value;
        }

        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var command in commands.Values)
        {
            command.Dispose();
        }
    }
}
