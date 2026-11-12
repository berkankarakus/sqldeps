using SqlDeps.Storage;

namespace SqlDeps;

internal static partial class CommandLine
{
    private static string ExistingFolder(string value)
    {
        var folder = Path.GetFullPath(value);
        if (!Directory.Exists(folder))
        {
            throw new ArgumentException("No such folder: " + folder);
        }

        return folder;
    }

    private static string DefaultDatabasePath(string folder)
    {
        return IndexPathResolver.DefaultDatabasePath(folder);
    }

    private static string ResolveDatabasePath(string value) => value.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || value.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(value) : DefaultDatabasePath(value);
}
