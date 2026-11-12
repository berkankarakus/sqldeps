using SqlDeps.Storage;

namespace SqlDeps;

internal static partial class CommandLine
{
    private static IndexDatabase OpenDatabase(string? databasePath) => new(databasePath ?? ActiveDatabasePathOrDefault());

    private static string IndexDirectory() => Path.GetDirectoryName(ActiveDatabasePath())!;

    private static string ActiveDatabasePathOrDefault()
    {
        return IndexPathResolver.ActiveDatabasePathOrDefault();
    }

    private static string ActiveDatabasePath()
    {
        return IndexPathResolver.ActivePointerPath();
    }
}
