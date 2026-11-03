namespace SqlDeps.Storage;

internal static class IndexPathResolver
{
    public static string ActiveDatabasePathOrDefault()
    {
        var activePath = ActivePointerPath();
        if (File.Exists(activePath))
        {
            var databasePath = File.ReadAllText(activePath).Trim();
            if (databasePath.Length > 0 && File.Exists(databasePath))
            {
                return databasePath;
            }
        }

        return DefaultDatabasePath(Directory.GetCurrentDirectory());
    }

    public static string ActivePointerPath()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("SQLDEPS_HOME");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.Combine(Path.GetFullPath(configuredDirectory), "active-index");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", "active-index");
    }

    public static string DefaultDatabasePath(string folder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", new DirectoryInfo(folder).Name + ".sqlite");
    }
}
