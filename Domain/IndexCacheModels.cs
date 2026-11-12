using SqlDeps.Domain;

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

/// <summary>Identifies one reusable batch contribution in the persistent index cache.</summary>
public sealed record CachedBatch(
    string SourceFile,
    int Ordinal,
    int StartLine,
    string TextHash,
    string Payload
);
