using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed partial class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache,
    int? jobs = null,
    Dictionary<string, List<CachedBatch>>? batchCache = null,
    string? defaultDatabase = null,
    IReadOnlyList<string>? includePatterns = null,
    IReadOnlyList<string>? excludePatterns = null
)
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxBatchCharacters = 16 * 1024 * 1024;
    private const int DefaultWorkerLimit = 4;

    private readonly IndexResultAccumulator results = new();

    /// <summary>The default schema used while collecting this workspace.</summary>
    public string DefaultSchema => defaultSchema;

    /// <summary>The default database used while collecting this workspace, when configured.</summary>
    public string? DefaultDatabase => defaultDatabase;

    /// <summary>The number of SQL files discovered.</summary>
    public int FileCount => results.FileCount;

    /// <summary>The number of files reused from the cache.</summary>
    public int ReusedFileCount => results.ReusedFileCount;

    /// <summary>Total parser time spent on files that were read.</summary>
    public TimeSpan ParseWork => results.ParseWork;

    /// <summary>Total dependency collection time.</summary>
    public TimeSpan CollectWork => results.CollectWork;

    /// <summary>Total script-generation time.</summary>
    public TimeSpan GenerateScriptWork => results.GenerateScriptWork;

    /// <summary>Number of script-generation calls.</summary>
    public int GenerateScriptCalls => results.GenerateScriptCalls;

    /// <summary>Number of source slices reused during parsing.</summary>
    public int SourceSliceHits => results.SourceSliceHits;

    /// <summary>Number of batches reused from the batch cache.</summary>
    public int ReusedBatchCount => results.ReusedBatchCount;

    /// <summary>Number of batches parsed during this run.</summary>
    public int ParsedBatchCount => results.ParsedBatchCount;

    /// <summary>Indexed file hashes and relative paths.</summary>
    public IReadOnlyList<IndexedFile> Files => results.Files;

    /// <summary>Objects defined by indexed SQL.</summary>
    public IReadOnlyList<DatabaseObject> Objects => results.Objects;

    /// <summary>Object-to-object dependency edges.</summary>
    public IReadOnlyList<Dependency> Dependencies => results.Dependencies;

    /// <summary>Procedure and function parameters.</summary>
    public IReadOnlyList<ObjectParameter> Parameters => results.Parameters;

    /// <summary>Procedure and function result columns.</summary>
    public IReadOnlyList<ResultColumn> ResultColumns => results.ResultColumns;

    /// <summary>Column reads and writes.</summary>
    public IReadOnlyList<ColumnUsage> ColumnUsage => results.ColumnUsage;

    /// <summary>Declared table columns.</summary>
    public IReadOnlyList<TableColumn> TableColumns => results.TableColumns;

    /// <summary>Primary-key and unique-key columns.</summary>
    public IReadOnlyList<TableKey> TableKeys => results.TableKeys;

    /// <summary>Foreign-key relationships.</summary>
    public IReadOnlyList<TableForeignKey> ForeignKeys => results.ForeignKeys;

    /// <summary>Index key and included columns.</summary>
    public IReadOnlyList<TableIndexColumn> IndexColumns => results.IndexColumns;

    /// <summary>Parser errors that prevented complete analysis.</summary>
    public IReadOnlyList<ParseProblem> Problems => results.Problems;

    /// <summary>Non-fatal indexing warnings.</summary>
    public IReadOnlyList<IndexWarning> Warnings => results.Warnings;

    /// <summary>Static-analysis findings.</summary>
    public IReadOnlyList<LintFinding> LintFindings => results.LintFindings;

    /// <summary>Objects explicitly named in unresolved dynamic SQL literal fragments.</summary>
    public IReadOnlyList<DynamicSqlCandidate> DynamicSqlCandidates => results.DynamicSqlCandidates;

    /// <summary>Parsed batch contributions.</summary>
    public IReadOnlyList<CachedBatch> Batches => results.Batches;

    /// <summary>Indexes SQL files below <paramref name="rootFolder" />, reusing unchanged file contributions.</summary>
    public void Index(string rootFolder, Action<IndexProgress>? progress = null)
    {
        string repositoryRoot = Path.GetFullPath(rootFolder);
        var files = FindSqlFiles(rootFolder, includePatterns, excludePatterns)
            .Select(file => (Path: file, RelativePath: Path.GetRelativePath(rootFolder, file)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        IndexedFileContribution[] results = new IndexedFileContribution[files.Length];
        var processed = 0;
        var reused = 0;
        progress?.Invoke(new IndexProgress("indexing", 0, files.Length, 0));

        var workerLimit = jobs ?? Math.Min(Environment.ProcessorCount, DefaultWorkerLimit);
        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = workerLimit }, index =>
        {
            var file = files[index];
            var result = IndexFile(file.Path, file.RelativePath, repositoryRoot);
            results[index] = result;
            if (result.WasReused)
            {
                Interlocked.Increment(ref reused);
            }

            var completed = Interlocked.Increment(ref processed);
            progress?.Invoke(new IndexProgress("indexing", completed, files.Length, Volatile.Read(ref reused)));
        });

        foreach (var result in results)
        {
            Merge(result);
        }
    }

    /// <summary>Enumerates SQL files while skipping generated and dependency directories.</summary>
    internal static IEnumerable<string> FindSqlFiles(
        string rootFolder,
        IReadOnlyList<string>? includePatterns = null,
        IReadOnlyList<string>? excludePatterns = null)
    {
        return SqlFileCatalog.FindSqlFiles(rootFolder, includePatterns, excludePatterns);
    }



    private void Merge(IndexedFileContribution result) => results.Merge(result);

}
