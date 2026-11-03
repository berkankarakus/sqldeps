using SqlDeps.Storage;

namespace SqlDeps.Lsp;

/// <summary>Finds a persisted index whose recorded source root matches the LSP workspace.</summary>
internal static class WorkspaceIndexLocator
{
    public static string? Find(string workspaceRoot)
    {
        var normalizedRoot = Normalize(workspaceRoot);

        foreach (var candidate in Candidates())
        {
            try
            {
                var database = new IndexDatabase(candidate);
                if (database.SourceRoot is { } sourceRoot && string.Equals(Normalize(sourceRoot), normalizedRoot, PathComparisonKind()))
                {
                    return candidate;
                }
            }
            catch (Exception) when (candidate.Length > 0)
            {
                // Ignore stale, incomplete, or legacy files while looking for a matching index.
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(PathComparer());
        var configuredHome = Environment.GetEnvironmentVariable("SQLDEPS_HOME");

        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            var configuredDirectory = Path.GetFullPath(configuredHome);
            foreach (var candidate in ActiveCandidate(configuredDirectory).Concat(SqliteFiles(configuredDirectory)))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "sqldeps");
        foreach (var candidate in ActiveCandidate(defaultDirectory).Concat(SqliteFiles(defaultDirectory)))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> ActiveCandidate(string directory)
    {
        var activePath = Path.Combine(directory, "active-index");
        if (!File.Exists(activePath))
        {
            return [];
        }

        try
        {
            var databasePath = File.ReadAllText(activePath).Trim();
            return databasePath.Length == 0 || !File.Exists(databasePath)
                ? []
                : [Path.GetFullPath(databasePath)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SqliteFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparisonKind() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
