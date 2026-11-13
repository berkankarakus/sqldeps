namespace SqlDeps.Domain;

/// <summary>Metadata collected from one parsed SQL script or batch.</summary>
public sealed record DependencyCollectionResult(
    IReadOnlyList<DatabaseObject> Objects,
    IReadOnlyList<Dependency> Dependencies,
    IReadOnlyList<ColumnUsage> ColumnUsage,
    IReadOnlyList<ObjectParameter> Parameters,
    IReadOnlyList<ResultColumn> ResultColumns,
    IReadOnlyList<TableKey> TableKeys,
    IReadOnlyList<TableIndexColumn> IndexColumns,
    IReadOnlyList<TableForeignKey> ForeignKeys,
    IReadOnlyList<TableColumn> TableColumns,
    IReadOnlyList<ParseProblem> Problems,
    IReadOnlyList<LintFinding> LintFindings,
    IReadOnlyList<DynamicSqlCandidate> DynamicSqlCandidates
);
