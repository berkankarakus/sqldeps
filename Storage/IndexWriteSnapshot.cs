using SqlDeps.Domain;

namespace SqlDeps.Storage;

/// <summary>Named input boundary for one atomic SQLite index write.</summary>
internal sealed class IndexWriteSnapshot
{
    internal IndexWriteSnapshot(
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

    internal IReadOnlyList<DatabaseObject> Objects { get; }
    internal IReadOnlyList<Dependency> Dependencies { get; }
    internal IReadOnlyList<ObjectParameter> Parameters { get; }
    internal IReadOnlyList<ResultColumn> ResultColumns { get; }
    internal IReadOnlyList<TableKey> TableKeys { get; }
    internal IReadOnlyList<TableForeignKey> ForeignKeys { get; }
    internal IReadOnlyList<TableIndexColumn> IndexColumns { get; }
    internal IReadOnlyList<ColumnUsage> ColumnUsage { get; }
    internal IReadOnlyList<TableColumn> TableColumns { get; }
    internal IReadOnlyList<ParseProblem> Problems { get; }
    internal IReadOnlyList<IndexWarning> Warnings { get; }
    internal IReadOnlyList<LintFinding> LintFindings { get; }
    internal IReadOnlyList<IndexedFile> Files { get; }
    internal ReachResult Reachability { get; }
    internal string Settings { get; }
    internal string DefaultSchema { get; }
    internal string SourceRoot { get; }
    internal IReadOnlyList<CachedBatch> Batches { get; }
}
