using SqlDeps.Storage;

namespace SqlDeps;

public static partial class CommandLine
{
    private static IndexDatabase OpenDatabase(string? databasePath) => new(databasePath ?? ActiveDatabasePathOrDefault());

    private static string IndexDirectory() => Path.GetDirectoryName(ActiveDatabasePath())!;

    private static string ActiveDatabasePathOrDefault()
    {
        var activePath = ActiveDatabasePath();
        if (File.Exists(activePath))
        {
            var databasePath = File.ReadAllText(activePath).Trim();
            if (databasePath != "" && File.Exists(databasePath)) return databasePath;
        }
        return DefaultDatabasePath(Directory.GetCurrentDirectory());
    }

    private static string ActiveDatabasePath()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("SQLDEPS_HOME");
        if (!string.IsNullOrWhiteSpace(configuredDirectory)) return Path.Combine(Path.GetFullPath(configuredDirectory), "active-index");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", "active-index");
    }
}
