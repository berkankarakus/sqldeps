using Microsoft.Data.Sqlite;
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
            if (MatchesWorkspaceRoot(TryGetSourceRoot(candidate), normalizedRoot))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? Find(
        string workspaceRoot,
        string? selectedIndexPath,
        bool allowAutomaticSelection
    )
    {
        if (!string.IsNullOrWhiteSpace(selectedIndexPath))
        {
            var normalizedPath = Path.GetFullPath(selectedIndexPath);
            return File.Exists(normalizedPath) ? normalizedPath : null;
        }

        return allowAutomaticSelection ? Find(workspaceRoot) : null;
    }

    public static string? SourceRoot(string databasePath)
    {
        return TryGetSourceRoot(databasePath);
    }

    private static string? TryGetSourceRoot(string databasePath)
    {
        try
        {
            return new IndexDatabase(databasePath).SourceRoot;
        }
        catch (SqliteException)
        {
            return null;
        }

        catch (InvalidOperationException)
        {
            return null;
        }

        catch (IOException)
        {
            return null;
        }

        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(PathComparer());

        foreach (var directory in CandidateDirectories())
        {
            foreach (var candidate in CandidatesIn(directory))
            {
                if (!seen.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var configuredHome = Environment.GetEnvironmentVariable("SQLDEPS_HOME");

        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            yield return Path.GetFullPath(configuredHome);
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "sqldeps");
    }

    private static IEnumerable<string> CandidatesIn(string directory)
    {
        return ActiveCandidate(directory).Concat(SqliteFiles(directory));
    }

    private static bool MatchesWorkspaceRoot(string? sourceRoot, string normalizedRoot)
    {
        return sourceRoot is not null
            && string.Equals(Normalize(sourceRoot), normalizedRoot, PathComparisonKind());
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
