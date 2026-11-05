using System.Diagnostics;

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
    private readonly List<IndexedFile> files = [];
    private long parseTicks;
    private long collectTicks;
    private long generateScriptTicks;

    public int FileCount => files.Count;

    public int ReusedFileCount { get; private set; }

    public TimeSpan ParseWork => TimeSpan.FromSeconds(parseTicks / (double)Stopwatch.Frequency);

    public TimeSpan CollectWork => TimeSpan.FromSeconds(collectTicks / (double)Stopwatch.Frequency);

    public TimeSpan GenerateScriptWork => TimeSpan.FromSeconds(generateScriptTicks / (double)Stopwatch.Frequency);

    public int GenerateScriptCalls { get; private set; }

    public int SourceSliceHits { get; private set; }

    public IReadOnlyList<IndexedFile> Files => files;

    public IReadOnlyList<DatabaseObject> Objects => objects;

    public IReadOnlyList<Dependency> Dependencies => dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => parameters;

    public IReadOnlyList<ResultColumn> ResultColumns => resultColumns;

    public IReadOnlyList<ColumnUsage> ColumnUsage => columnUsage;

    public IReadOnlyList<TableColumn> TableColumns => tableColumns;

    public IReadOnlyList<TableKey> TableKeys => tableKeys;

    public IReadOnlyList<TableForeignKey> ForeignKeys => foreignKeys;

    public IReadOnlyList<TableIndexColumn> IndexColumns => indexColumns;

    public IReadOnlyList<ParseProblem> Problems => problems;

    public IReadOnlyList<IndexWarning> Warnings => warnings;

    public IReadOnlyList<LintFinding> LintFindings => lintFindings;

    public void Merge(IndexedFileContribution result)
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
            finding.SourceFile,
            finding.Line,
            finding.Message)));
        ReusedFileCount += result.Reused ? 1 : 0;
        parseTicks += result.ParseTicks;
        collectTicks += result.CollectTicks;
        generateScriptTicks += result.GenerateScriptTicks;
        GenerateScriptCalls += result.GenerateScriptCalls;
        SourceSliceHits += result.SourceSliceHits;
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
