using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps;

/// <summary>
/// Reads the previous index back. The cache is only trusted when it was built with the same settings
/// (the default schema/database and discovery scope) and the same version of the indexing rules; otherwise everything is read again.
/// </summary>
public static partial class IndexCache
{
    /// <summary>Bump when the rules that turn a script into dependencies change, so old caches are dropped.</summary>
    public const int RulesVersion = 21;

    /// <summary>Builds the cache compatibility key for parser, name-resolution, suppression, and discovery settings.</summary>
    public static string SettingsFor(
        string defaultSchema,
        string? repositoryRoot = null,
        string? defaultDatabase = null,
        IReadOnlyList<string>? includePatterns = null,
        IReadOnlyList<string>? excludePatterns = null)
    {
        var database = defaultDatabase ?? "";
        var includes = string.Join("\u001f", includePatterns ?? []);
        var excludes = string.Join("\u001f", excludePatterns ?? []);

        return $"rules={RulesVersion};default_schema={defaultSchema};default_database={database};includes={includes};excludes={excludes};repository_suppression={RepositorySuppressionHash(repositoryRoot)};ignore={IgnoreFileHash(repositoryRoot)}";
    }

    private static string RepositorySuppressionHash(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return "none";
        }

        string path = Path.Combine(repositoryRoot, ".sqldeps");

        if (!File.Exists(path))
        {
            return "none";
        }

        return SqlFileReader.HashOf(File.ReadAllBytes(path));
    }

    private static string IgnoreFileHash(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return "none";
        }

        string path = Path.Combine(repositoryRoot, ".sqldepsignore");

        return File.Exists(path) ? SqlFileReader.HashOf(File.ReadAllBytes(path)) : "none";
    }

    /// <summary>Loads per-file contributions when the database schema and settings are compatible.</summary>
    public static Dictionary<string, CachedFile> Load(
        string databasePath,
        string settings
    )
    {
        return LoadData(databasePath, settings, loadFiles: true, loadBatches: false).Files;
    }

    /// <summary>Loads reusable batch contributions when the database is compatible.</summary>
    public static Dictionary<string, List<CachedBatch>> LoadBatches(
        string databasePath,
        string settings)
    {
        return LoadData(databasePath, settings, loadFiles: false, loadBatches: true).Batches;
    }

    /// <summary>Loads file and batch contributions through one compatible SQLite connection.</summary>
    public static (
        Dictionary<string, CachedFile> Files,
        Dictionary<string, List<CachedBatch>> Batches) LoadAll(
        string databasePath,
        string settings)
    {
        return LoadData(databasePath, settings, loadFiles: true, loadBatches: true);
    }
}
