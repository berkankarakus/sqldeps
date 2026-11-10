using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps;

/// <summary>What one file contributed to the previous index, so an unchanged file need not be parsed again.</summary>
public sealed record CachedFile(
    string Hash,
    List<DatabaseObject> Objects,
    List<Dependency> Dependencies,
    List<ObjectParameter> Parameters,
    List<ResultColumn> ResultColumns,
    List<TableKey> TableKeys,
    List<TableForeignKey> ForeignKeys,
    List<TableIndexColumn> IndexColumns,
    List<ColumnUsage> ColumnUsage,
    List<TableColumn> TableColumns,
    List<ParseProblem> Problems,
    List<IndexWarning> Warnings,
    List<LintFinding> LintFindings
);

/// <summary>
/// Reads the previous index back. The cache is only trusted when it was built with the same settings
/// (the default schema) and the same version of the indexing rules; otherwise everything is read again.
/// </summary>
public static partial class IndexCache
{
    /// <summary>Bump when the rules that turn a script into dependencies change, so old caches are dropped.</summary>
    public const int RulesVersion = 20;

    /// <summary>Builds the cache compatibility key for parser, name-resolution, and suppression settings.</summary>
    public static string SettingsFor(string defaultSchema, string? repositoryRoot = null)
    {
        return $"rules={RulesVersion};default_schema={defaultSchema};repository_suppression={RepositorySuppressionHash(repositoryRoot)}";
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
