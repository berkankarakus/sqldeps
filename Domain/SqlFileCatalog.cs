namespace SqlDeps;

/// <summary>Provides the shared SQL-file discovery policy for indexing and index freshness checks.</summary>
public static class SqlFileCatalog
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules"
    };

    /// <summary>Enumerates .sql files recursively while applying the repository discovery policy.</summary>
    public static IEnumerable<string> FindSqlFiles(
        string rootFolder,
        IReadOnlyList<string>? includePatterns = null,
        IReadOnlyList<string>? excludePatterns = null)
    {
        var effectiveExcludes = ReadIgnorePatterns(rootFolder)
            .Concat(excludePatterns ?? [])
            .Where(pattern => pattern.Length > 0)
            .ToArray();

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.sql", options))
        {
            var relativePath = Path.GetRelativePath(rootFolder, file);
            if (IsInSkippedFolder(relativePath))
            {
                continue;
            }

            if (includePatterns is not null
                && includePatterns.Count > 0
                && !includePatterns.Any(pattern => Matches(relativePath, pattern)))
            {
                continue;
            }

            if (effectiveExcludes.Any(pattern => Matches(relativePath, pattern)))
            {
                continue;
            }

            yield return file;
        }
    }

    /// <summary>Reads optional repository-local discovery exclusions without reading SQL contents.</summary>
    public static IReadOnlyList<string> ReadIgnorePatterns(string rootFolder)
    {
        var path = Path.Combine(rootFolder, ".sqldepsignore");

        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    private static bool IsInSkippedFolder(string relativePath)
    {
        var folderStart = 0;

        while (true)
        {
            var separator = relativePath.IndexOf(Path.DirectorySeparatorChar, folderStart);

            if (separator < 0)
            {
                return false;
            }

            var folder = relativePath.AsSpan(folderStart, separator - folderStart);
            if (SkippedFolders.Contains(folder.ToString()))
            {
                return true;
            }

            folderStart = separator + 1;
        }
    }

    private static bool Matches(string relativePath, string pattern)
    {
        var normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var normalizedPattern = pattern.Trim().Replace('\\', '/');

        if (normalizedPattern.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedPattern = normalizedPattern[2..];
        }

        normalizedPattern = normalizedPattern.TrimStart('/');

        if (normalizedPattern.Length == 0)
        {
            return false;
        }

        var regex = GlobRegex(normalizedPattern);

        return System.Text.RegularExpressions.Regex.IsMatch(
            normalizedPath,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string GlobRegex(string pattern)
    {
        var prefix = pattern.StartsWith("**/", StringComparison.Ordinal) ? "(?:.*/)?" : "";
        var suffix = pattern.EndsWith("/**", StringComparison.Ordinal) ? "(?:/.*)?" : "";
        var body = pattern;

        if (prefix.Length > 0)
        {
            body = body[3..];
        }

        if (suffix.Length > 0)
        {
            body = body[..^3];
        }

        System.Text.StringBuilder result = new("^");
        result.Append(prefix);
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '*' && index + 1 < body.Length && body[index + 1] == '*')
            {
                result.Append(".*");
                index++;
            }
            else if (character == '*')
            {
                result.Append("[^/]*");
            }
            else if (character == '?')
            {
                result.Append("[^/]");
            }
            else
            {
                result.Append(System.Text.RegularExpressions.Regex.Escape(character.ToString()));
            }
        }

        result.Append(suffix).Append('$');

        return result.ToString();
    }
}
