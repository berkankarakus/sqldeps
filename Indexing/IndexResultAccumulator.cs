using System.Diagnostics;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Accumulates deterministic folder-level results from independently indexed files.</summary>
internal sealed class IndexResultAccumulator
{
    private readonly List<DatabaseObject> objects = [];
    private readonly List<Dependency> dependencies = [];
    private readonly List<ObjectParameter> parameters = [];
    private readonly List<ResultColumn> resultColumns = [];
    private readonly List<ColumnUsage> columnUsage = [];
    private readonly List<TableColumn> tableColumns = [];
    private readonly List<TableKey> tableKeys = [];
    private readonly List<TableForeignKey> foreignKeys = [];
    private readonly List<TableIndexColumn> indexColumns = [];
    private readonly HashSet<string> tableColumnKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ParseProblem> problems = [];
    private readonly List<IndexWarning> warnings = [];
    private readonly List<LintFinding> lintFindings = [];
    private readonly List<DynamicSqlCandidate> dynamicSqlCandidates = [];
    private readonly List<CachedBatch> batches = [];
    private readonly List<IndexedFile> files = [];
    private long parseTicks;
    private long collectTicks;
    private long generateScriptTicks;

    internal int FileCount => files.Count;

    internal int ReusedFileCount { get; private set; }

    internal TimeSpan ParseWork => TimeSpan.FromSeconds(parseTicks / (double)Stopwatch.Frequency);

    internal TimeSpan CollectWork => TimeSpan.FromSeconds(collectTicks / (double)Stopwatch.Frequency);

    internal TimeSpan GenerateScriptWork => TimeSpan.FromSeconds(generateScriptTicks / (double)Stopwatch.Frequency);

    internal int GenerateScriptCalls { get; private set; }

    internal int SourceSliceHits { get; private set; }

    internal int ReusedBatchCount { get; private set; }

    internal int ParsedBatchCount { get; private set; }

    internal IReadOnlyList<IndexedFile> Files => files;

    internal IReadOnlyList<DatabaseObject> Objects => objects;

    internal IReadOnlyList<Dependency> Dependencies => dependencies;

    internal IReadOnlyList<ObjectParameter> Parameters => parameters;

    internal IReadOnlyList<ResultColumn> ResultColumns => resultColumns;

    internal IReadOnlyList<ColumnUsage> ColumnUsage => columnUsage;

    internal IReadOnlyList<TableColumn> TableColumns => tableColumns;

    internal IReadOnlyList<TableKey> TableKeys => tableKeys;

    internal IReadOnlyList<TableForeignKey> ForeignKeys => foreignKeys;

    internal IReadOnlyList<TableIndexColumn> IndexColumns => indexColumns;

    internal IReadOnlyList<ParseProblem> Problems => problems;

    internal IReadOnlyList<IndexWarning> Warnings => warnings;

    internal IReadOnlyList<LintFinding> LintFindings => lintFindings;

    internal IReadOnlyList<DynamicSqlCandidate> DynamicSqlCandidates => dynamicSqlCandidates;

    internal IReadOnlyList<CachedBatch> Batches => batches;

    internal void Merge(IndexedFileContribution result)
    {
        files.Add(result.File);
        objects.AddRange(result.Objects);
        dependencies.AddRange(result.Dependencies);
        parameters.AddRange(result.Parameters);
        resultColumns.AddRange(result.ResultColumns);
        tableKeys.AddRange(result.TableKeys);
        foreignKeys.AddRange(result.ForeignKeys);
        indexColumns.AddRange(result.IndexColumns);
        columnUsage.AddRange(result.ColumnUsage);

        MergeTableColumns(result.TableColumns);

        problems.AddRange(result.Problems);
        warnings.AddRange(result.Warnings);
        lintFindings.AddRange(result.LintFindings.DistinctBy(finding => (
            finding.Rule,
            finding.Severity,
            finding.SourceObject,
            finding.SourceFile,
            finding.Line,
            finding.Column,
            finding.Length,
            finding.Message)));
        dynamicSqlCandidates.AddRange(result.DynamicSqlCandidates.DistinctBy(candidate => (
            candidate.SourceObject,
            candidate.CandidateObject,
            candidate.Operation,
            candidate.SourceFile,
            candidate.Line)));
        batches.AddRange(result.Batches);
        ReusedFileCount += result.WasReused ? 1 : 0;
        parseTicks += result.ParseTicks;
        collectTicks += result.CollectTicks;
        generateScriptTicks += result.GenerateScriptTicks;
        GenerateScriptCalls += result.GenerateScriptCalls;
        SourceSliceHits += result.SourceSliceHits;
        ReusedBatchCount += result.ReusedBatchCount;
        ParsedBatchCount += result.ParsedBatchCount;
    }

    private void MergeTableColumns(IEnumerable<TableColumn> columns)
    {
        foreach (TableColumn column in columns)
        {
            if (tableColumnKeys.Add(column.TableName + "\0" + column.ColumnName))
            {
                tableColumns.Add(column);
            }
        }
    }
}
