using System.Diagnostics;
using SqlDeps.Analysis;
using SqlDeps.Cli;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps;

public static partial class CommandLine
{
    private static int Index(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = Parse(args, requireValue: true, allowSchema: true, allowFull: true, allowJobs: true);
        var folder = ExistingFolder(options.Value!);
        var databasePath = options.DatabasePath ?? DefaultDatabasePath(folder);
        var defaultSchema = options.DefaultSchema ?? ObjectNames.StandardDefaultSchema;
        var settings = IndexCache.SettingsFor(defaultSchema, folder);
        (Dictionary<string, CachedFile> cache, Dictionary<string, List<CachedBatch>> batchCache) = options.FullReindex
            ? ([], [])
            : IndexCache.LoadAll(databasePath, settings);
        SqlFolderIndexer indexer = new(defaultSchema, cache, options.Jobs, batchCache);
        var progress = ConsoleProgressRenderer.Create(options.Json, options.Quiet);

        try
        {
            indexer.Index(folder, progress is null ? null : progress.Report);
            var indexedAt = stopwatch.Elapsed;
            progress?.Report(new IndexProgress("reachability", 0, 0, indexer.ReusedFileCount));
            var reachability = ReachabilityBuilder.Build(indexer.Dependencies, MaxReachDepth);
            var reachedAt = stopwatch.Elapsed;
            progress?.Report(new IndexProgress("SQLite", 0, 0, indexer.ReusedFileCount));

            SqliteStore.Write(databasePath, indexer.Objects, indexer.Dependencies, indexer.Parameters, indexer.ResultColumns, indexer.TableKeys, indexer.ForeignKeys, indexer.IndexColumns, indexer.ColumnUsage, indexer.TableColumns, indexer.Problems, indexer.Warnings, indexer.LintFindings, indexer.Files, reachability, settings, defaultSchema, folder, indexer.Batches);
            stopwatch.Stop();
            progress?.Complete();

            if (options.Json)
            {
                ConsoleOutput.WriteIndexSummary(indexer.FileCount, indexer.ReusedFileCount, indexer.Objects.Count, indexer.Dependencies.Count, indexer.Problems.Count, indexer.Warnings.Count, reachability.Reach.Count, reachability.Cycles.Count, databasePath);
            }
            else if (!options.Quiet)
            {
                Console.WriteLine("Indexed {0} .sql files ({1} reused unchanged): {2} objects, {3} dependencies, {4} parse errors, {5} warnings.", indexer.FileCount, indexer.ReusedFileCount, indexer.Objects.Count, indexer.Dependencies.Count, indexer.Problems.Count, indexer.Warnings.Count);
                Console.WriteLine("Reach: {0} object pairs (up to {1} steps), {2} objects in a cycle.", reachability.Reach.Count, MaxReachDepth, reachability.Cycles.Count);
                Console.WriteLine("Timing: parse {0:F2}s, reach {1:F2}s, SQLite {2:F2}s", indexedAt.TotalSeconds, (reachedAt - indexedAt).TotalSeconds, (stopwatch.Elapsed - reachedAt).TotalSeconds);
                Console.WriteLine("Work: ScriptDom parse {0:F2}s, collect {1:F2}s (sum across workers)", indexer.ParseWork.TotalSeconds, indexer.CollectWork.TotalSeconds);
                Console.WriteLine("Batches: {0} reused, {1} parsed", indexer.ReusedBatchCount, indexer.ParsedBatchCount);
                Console.WriteLine("GenerateScript: {0:F2}s, {1} calls, {2} source slices", indexer.GenerateScriptWork.TotalSeconds, indexer.GenerateScriptCalls, indexer.SourceSliceHits);
                Console.WriteLine("Elapsed: {0:F2}s", stopwatch.Elapsed.TotalSeconds);
                Console.WriteLine("Database: " + databasePath);
            }
        }
        finally
        {
            progress?.Complete();
        }

        return 0;
    }
}
