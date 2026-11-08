using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache,
    int? jobs = null,
    Dictionary<string, List<CachedBatch>>? batchCache = null
)
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxBatchCharacters = 16 * 1024 * 1024;
    private const int DefaultWorkerLimit = 4;

    private readonly IndexResultAccumulator results = new();

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

    /// <summary>Parsed batch contributions.</summary>
    public IReadOnlyList<CachedBatch> Batches => results.Batches;

    /// <summary>Indexes SQL files below <paramref name="rootFolder" />, reusing unchanged file contributions.</summary>
    public void Index(string rootFolder, Action<IndexProgress>? progress = null)
    {
        string repositoryRoot = Path.GetFullPath(rootFolder);
        var files = FindSqlFiles(rootFolder)
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
    public static IEnumerable<string> FindSqlFiles(string rootFolder)
    {
        return SqlFileCatalog.FindSqlFiles(rootFolder);
    }

    private IndexedFileContribution IndexFile(
        string path,
        string relativePath,
        string repositoryRoot
    )
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            return OversizedFile(relativePath);
        }

        var bytes = File.ReadAllBytes(path);
        var hash = SqlFileReader.HashOf(bytes);
        var indexedFile = new IndexedFile(relativePath, hash);

        if (TryGetCachedContribution(relativePath, indexedFile, hash, out var cachedContribution))
        {
            return cachedContribution;
        }

        IndexedFileContribution result = new(indexedFile);
        var sourceText = SqlFileReader.Decode(bytes);
        IndexBatches(sourceText, relativePath, result, repositoryRoot);

        return result;
    }

    private bool TryGetCachedContribution(
        string relativePath,
        IndexedFile indexedFile,
        string hash,
        out IndexedFileContribution contribution)
    {
        contribution = null!;

        if (!cache.TryGetValue(relativePath, out var cached) || cached.Hash != hash)
        {
            return false;
        }

        contribution = IndexedFileContribution.FromCache(indexedFile, cached);

        if (batchCache is not null && batchCache.TryGetValue(relativePath, out List<CachedBatch>? cachedBatches))
        {
            contribution.Batches.AddRange(cachedBatches);
        }

        return true;
    }

    private void IndexBatches(
        string sourceText,
        string relativePath,
        IndexedFileContribution result,
        string repositoryRoot)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        IReadOnlyList<SqlBatch> batches = BatchSplitter.Split(sourceText);
        IReadOnlyList<CachedBatch> cachedBatches = batchCache?.GetValueOrDefault(relativePath) ?? [];
        Dictionary<int, CachedBatch> cachedBatchesByOrdinal = CreateBatchLookup(cachedBatches);

        for (int ordinal = 0; ordinal < batches.Count; ordinal++)
        {
            SqlBatch batch = batches[ordinal];
            IndexedFileContribution batchResult = new(result.File);
            IndexBatchContribution(
                batch,
                ordinal,
                relativePath,
                parser,
                sourceText,
                repositoryRoot,
                cachedBatchesByOrdinal,
                batchResult);

            result.Merge(batchResult);
            result.Batches.Add(new CachedBatch(
                relativePath,
                ordinal,
                batch.StartLine,
                BatchContributionCache.HashOf(batch.Text),
                BatchContributionCache.Serialize(batchResult)));
        }
    }

    private void IndexBatchContribution(
        SqlBatch batch,
        int ordinal,
        string relativePath,
        TSql160Parser parser,
        string sourceText,
        string repositoryRoot,
        IReadOnlyDictionary<int, CachedBatch> cachedBatches,
        IndexedFileContribution result)
    {
        if (batch.Text.Length > MaxBatchCharacters)
        {
            result.Problems.Add(new ParseProblem(
                relativePath,
                batch.StartLine,
                $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."));

            return;
        }

        if (TryReuseBatch(cachedBatches, ordinal, batch, result.File, result))
        {
            result.ReusedBatchCount++;

            return;
        }

        IndexBatch(batch, relativePath, parser, result, sourceText, repositoryRoot);
        result.ParsedBatchCount++;
    }

    private static Dictionary<int, CachedBatch> CreateBatchLookup(IReadOnlyList<CachedBatch> cachedBatches)
    {
        Dictionary<int, CachedBatch> batchesByOrdinal = [];

        foreach (CachedBatch cachedBatch in cachedBatches)
        {
            batchesByOrdinal[cachedBatch.Ordinal] = cachedBatch;
        }

        return batchesByOrdinal;
    }

    private static bool TryReuseBatch(
        IReadOnlyDictionary<int, CachedBatch> cachedBatches,
        int ordinal,
        SqlBatch batch,
        IndexedFile file,
        IndexedFileContribution result)
    {
        if (!cachedBatches.TryGetValue(ordinal, out CachedBatch? cached))
        {
            return false;
        }

        if (cached is null)
        {
            return false;
        }

        if (!BatchContributionCache.Matches(cached, file.Path, ordinal, batch.StartLine, batch.Text))
        {
            return false;
        }

        if (!BatchContributionCache.TryDeserialize(cached, file, out IndexedFileContribution? cachedResult))
        {
            return false;
        }

        if (cachedResult is null)
        {
            return false;
        }

        result.Merge(cachedResult);

        return true;
    }

    private static IndexedFileContribution OversizedFile(string relativePath)
    {
        IndexedFileContribution result = new(new IndexedFile(relativePath, ""));
        result.Problems.Add(new ParseProblem(relativePath, 1, $"SQL file exceeds the {MaxFileBytes / (1024 * 1024)} MiB limit."));

        return result;
    }

    private void IndexBatch(
        SqlBatch batch,
        string relativePath,
        TSql160Parser parser,
        IndexedFileContribution result,
        string fullSourceText,
        string repositoryRoot
    )
    {
        var parseStarted = Stopwatch.GetTimestamp();
        var script = ParseBatch(batch, parser, out var errors);
        result.ParseTicks += Stopwatch.GetTimestamp() - parseStarted;

        if (errors.Count > 0)
        {
            HandleParseErrors(batch, relativePath, errors, fullSourceText, repositoryRoot, result);

            return;
        }

        if (script is null)
        {
            return;
        }

        var collectStarted = Stopwatch.GetTimestamp();
        CollectScript(script, batch.Text, fullSourceText, relativePath, batch.StartLine, repositoryRoot, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;
    }

    private void HandleParseErrors(
        SqlBatch batch,
        string relativePath,
        IList<ParseError> errors,
        string fullSourceText,
        string repositoryRoot,
        IndexedFileContribution result
    )
    {
        if (!IsUserScript(relativePath))
        {
            AddParseErrors(batch, relativePath, errors, result);

            return;
        }

        if (TryIndexValidPrefix(batch, relativePath, errors[0].Line, fullSourceText, repositoryRoot, result))
        {
            result.Warnings.Add(new IndexWarning(
                relativePath,
                batch.StartLine + errors[0].Line - 1,
                "Non-SQL text after a valid SQL prefix was skipped."));

            return;
        }

        AddParseErrors(batch, relativePath, errors, result);
    }

    private static TSqlScript? ParseBatch(SqlBatch batch, TSql160Parser parser, out IList<ParseError> errors)
    {
        using StringReader reader = new(batch.Text);
        var fragment = parser.Parse(reader, out errors);

        return fragment as TSqlScript;
    }

    private static void AddParseErrors(
        SqlBatch batch,
        string relativePath,
        IEnumerable<ParseError> errors,
        IndexedFileContribution result
    )
    {
        foreach (var error in errors)
        {
            result.Problems.Add(new ParseProblem(relativePath, batch.StartLine + error.Line - 1, error.Message));
        }
    }

    private bool TryIndexValidPrefix(
        SqlBatch batch,
        string relativePath,
        int errorLine,
        string fullSourceText,
        string repositoryRoot,
        IndexedFileContribution result)
    {
        var prefix = string.Join('\n', batch.Text.Split('\n').Take(errorLine - 1));

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(prefix);
        var parseStarted = Stopwatch.GetTimestamp();
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        result.ParseTicks += Stopwatch.GetTimestamp() - parseStarted;

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return false;
        }

        var collectStarted = Stopwatch.GetTimestamp();
        CollectScript(script, prefix, fullSourceText, relativePath, batch.StartLine, repositoryRoot, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;

        return true;
    }

    private void CollectScript(
        TSqlScript script,
        string sourceText,
        string fullSourceText,
        string relativePath,
        int batchStartLine,
        string? repositoryRoot,
        IndexedFileContribution result
    )
    {
        var cteNames = CteNameCollector.Collect(script);
        DependencyCollector collector = new(relativePath, batchStartLine, cteNames, defaultSchema, sourceText);
        collector.Collect(script);
        MergeCollectorResult(result, collector, fullSourceText, batchStartLine, repositoryRoot);
    }

    private static void MergeCollectorResult(
        IndexedFileContribution result,
        DependencyCollector collector,
        string fullSourceText,
        int batchStartLine,
        string? repositoryRoot
    )
    {
        result.GenerateScriptTicks += collector.GenerateScriptTicks;
        result.GenerateScriptCalls += collector.GenerateScriptCalls;
        result.SourceSliceHits += collector.SourceSliceHits;
        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.Parameters.AddRange(collector.Parameters);
        result.ResultColumns.AddRange(collector.ResultColumns);
        result.TableKeys.AddRange(collector.TableKeys);
        result.ForeignKeys.AddRange(collector.ForeignKeys);
        result.IndexColumns.AddRange(collector.IndexColumns);
        result.ColumnUsage.AddRange(collector.ColumnUsage);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(
            collector.LintFindings,
            fullSourceText,
            batchStartLine,
            fullSourceText,
            repositoryRoot));
    }

    private static bool IsUserScript(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => string.Equals(part, "UserScripts", StringComparison.OrdinalIgnoreCase));
    }

    private void Merge(IndexedFileContribution result) => results.Merge(result);

}
