namespace SqlDeps.Storage;

/// <summary>Resolves the active and default per-user index locations.</summary>
public static class IndexPathResolver
{
    /// <summary>Returns the active index path, or the current-folder default when none is configured.</summary>
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

    /// <summary>Returns the path of the active-index pointer file.</summary>
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

    /// <summary>Returns the default index path for a source folder.</summary>
    public static string DefaultDatabasePath(string folder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", new DirectoryInfo(folder).Name + ".sqlite");
    }
}
