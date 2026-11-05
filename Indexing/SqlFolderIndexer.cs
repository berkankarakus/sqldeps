using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Indexing;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache,
    int? jobs = null
)
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxBatchCharacters = 16 * 1024 * 1024;
    private const int DefaultWorkerLimit = 4;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules"
    };

    private readonly IndexResultAccumulator results = new();

    public int FileCount => results.FileCount;

    public int ReusedFileCount => results.ReusedFileCount;

    public TimeSpan ParseWork => results.ParseWork;

    public TimeSpan CollectWork => results.CollectWork;

    public TimeSpan GenerateScriptWork => results.GenerateScriptWork;

    public int GenerateScriptCalls => results.GenerateScriptCalls;

    public int SourceSliceHits => results.SourceSliceHits;

    public IReadOnlyList<IndexedFile> Files => results.Files;

    public IReadOnlyList<DatabaseObject> Objects => results.Objects;

    public IReadOnlyList<Dependency> Dependencies => results.Dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => results.Parameters;

    public IReadOnlyList<ResultColumn> ResultColumns => results.ResultColumns;

    public IReadOnlyList<ColumnUsage> ColumnUsage => results.ColumnUsage;

    public IReadOnlyList<TableColumn> TableColumns => results.TableColumns;

    public IReadOnlyList<TableKey> TableKeys => results.TableKeys;

    public IReadOnlyList<TableForeignKey> ForeignKeys => results.ForeignKeys;

    public IReadOnlyList<TableIndexColumn> IndexColumns => results.IndexColumns;

    public IReadOnlyList<ParseProblem> Problems => results.Problems;

    public IReadOnlyList<IndexWarning> Warnings => results.Warnings;

    public IReadOnlyList<LintFinding> LintFindings => results.LintFindings;

    public void Index(string rootFolder, Action<IndexProgress>? progress = null)
    {
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
            var result = IndexFile(file.Path, file.RelativePath);
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

    public static IEnumerable<string> FindSqlFiles(string rootFolder)
    {
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

            yield return file;
        }
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

            foreach (var skippedFolder in SkippedFolders)
            {
                if (folder.Equals(skippedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            folderStart = separator + 1;
        }
    }

    private IndexedFileContribution IndexFile(
        string path,
        string relativePath
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
        IndexBatches(bytes, relativePath, result);

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

        return true;
    }

    private void IndexBatches(
        byte[] bytes,
        string relativePath,
        IndexedFileContribution result)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        foreach (var batch in BatchSplitter.Split(SqlFileReader.Decode(bytes)))
        {
            if (batch.Text.Length > MaxBatchCharacters)
            {
                result.Problems.Add(new ParseProblem(
                    relativePath,
                    batch.StartLine,
                    $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."));

                continue;
            }

            IndexBatch(batch, relativePath, parser, result);
        }
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
        IndexedFileContribution result
    )
    {
        var parseStarted = Stopwatch.GetTimestamp();
        var script = ParseBatch(batch, parser, out var errors);
        result.ParseTicks += Stopwatch.GetTimestamp() - parseStarted;

        if (errors.Count > 0)
        {
            HandleParseErrors(batch, relativePath, errors, result);

            return;
        }

        if (script is null)
        {
            return;
        }

        var collectStarted = Stopwatch.GetTimestamp();
        CollectScript(script, batch.Text, relativePath, batch.StartLine, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;
    }

    private void HandleParseErrors(
        SqlBatch batch,
        string relativePath,
        IList<ParseError> errors,
        IndexedFileContribution result
    )
    {
        if (IsUserScript(relativePath) && TryIndexValidPrefix(batch, relativePath, errors[0].Line, result))
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

    private bool TryIndexValidPrefix(SqlBatch batch, string relativePath, int errorLine, IndexedFileContribution result)
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
        CollectScript(script, prefix, relativePath, batch.StartLine, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;

        return true;
    }

    private void CollectScript(
        TSqlScript script,
        string sourceText,
        string relativePath,
        int batchStartLine,
        IndexedFileContribution result
    )
    {
        var cteNames = CteNameCollector.Collect(script);
        DependencyCollector collector = new(relativePath, batchStartLine, cteNames, defaultSchema, sourceText);
        collector.Collect(script);
        MergeCollectorResult(result, collector, sourceText, batchStartLine);
    }

    private static void MergeCollectorResult(
        IndexedFileContribution result,
        DependencyCollector collector,
        string sourceText,
        int batchStartLine
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
        result.LintFindings.AddRange(LintSuppressions.Filter(collector.LintFindings, sourceText, batchStartLine));
    }

    private static bool IsUserScript(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => string.Equals(part, "UserScripts", StringComparison.OrdinalIgnoreCase));
    }

    private void Merge(IndexedFileContribution result) => results.Merge(result);

}
