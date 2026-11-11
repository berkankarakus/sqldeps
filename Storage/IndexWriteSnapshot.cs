using SqlDeps.Domain;

namespace SqlDeps.Storage;

/// <summary>Named input boundary for one atomic SQLite index write.</summary>
internal sealed class IndexWriteSnapshot
{
    public IndexWriteSnapshot(
        IReadOnlyList<DatabaseObject> objects,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<ObjectParameter> parameters,
        IReadOnlyList<ResultColumn> resultColumns,
        IReadOnlyList<TableKey> tableKeys,
        IReadOnlyList<TableForeignKey> foreignKeys,
        IReadOnlyList<TableIndexColumn> indexColumns,
        IReadOnlyList<ColumnUsage> columnUsage,
        IReadOnlyList<TableColumn> tableColumns,
        IReadOnlyList<ParseProblem> problems,
        IReadOnlyList<IndexWarning> warnings,
        IReadOnlyList<LintFinding> lintFindings,
        IReadOnlyList<IndexedFile> files,
        ReachResult reachability,
        string settings,
        string defaultSchema,
        string sourceRoot,
        IReadOnlyList<CachedBatch>? batches = null)
    {
        Objects = objects;
        Dependencies = dependencies;
        Parameters = parameters;
        ResultColumns = resultColumns;
        TableKeys = tableKeys;
        ForeignKeys = foreignKeys;
        IndexColumns = indexColumns;
        ColumnUsage = columnUsage;
        TableColumns = tableColumns;
        Problems = problems;
        Warnings = warnings;
        LintFindings = lintFindings;
        Files = files;
        Reachability = reachability;
        Settings = settings;
        DefaultSchema = defaultSchema;
        SourceRoot = sourceRoot;
        Batches = batches ?? [];
    }

    public IReadOnlyList<DatabaseObject> Objects { get; }
    public IReadOnlyList<Dependency> Dependencies { get; }
    public IReadOnlyList<ObjectParameter> Parameters { get; }
    public IReadOnlyList<ResultColumn> ResultColumns { get; }
    public IReadOnlyList<TableKey> TableKeys { get; }
    public IReadOnlyList<TableForeignKey> ForeignKeys { get; }
    public IReadOnlyList<TableIndexColumn> IndexColumns { get; }
    public IReadOnlyList<ColumnUsage> ColumnUsage { get; }
    public IReadOnlyList<TableColumn> TableColumns { get; }
    public IReadOnlyList<ParseProblem> Problems { get; }
    public IReadOnlyList<IndexWarning> Warnings { get; }
    public IReadOnlyList<LintFinding> LintFindings { get; }
    public IReadOnlyList<IndexedFile> Files { get; }
    public ReachResult Reachability { get; }
    public string Settings { get; }
    public string DefaultSchema { get; }
    public string SourceRoot { get; }
    public IReadOnlyList<CachedBatch> Batches { get; }
}
