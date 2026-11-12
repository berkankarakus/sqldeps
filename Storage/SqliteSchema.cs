using Microsoft.Data.Sqlite;
using System.Globalization;

namespace SqlDeps.Storage;

/// <summary>Shared SQLite schema contract used by the writer and incremental cache reader.</summary>
internal static class SqliteSchema
{
    internal const int Version = SqliteStore.SchemaVersion;

    internal static bool IsCompatible(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        var value = command.ExecuteScalar() as string;

        return string.Equals(value, Version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
