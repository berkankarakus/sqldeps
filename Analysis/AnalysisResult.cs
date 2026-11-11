using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal sealed class AnalysisResult(string sourceFile, string text = "")
{
    public string SourceFile { get; } = sourceFile;
    public string Text { get; } = text;
    public List<DatabaseObject> Objects { get; } = [];
    public List<Dependency> Dependencies { get; } = [];
    public List<ColumnUsage> ColumnUsage { get; } = [];
    public List<ObjectParameter> Parameters { get; } = [];
    public List<ResultColumn> ResultColumns { get; } = [];
    public List<TableKey> TableKeys { get; } = [];
    public List<TableIndexColumn> IndexColumns { get; } = [];
    public List<TableForeignKey> ForeignKeys { get; } = [];
    public List<IndexedFile> Files { get; } = [];
    public List<TableColumn> TableColumns { get; } = [];
    public List<LocalColumn> LocalColumns { get; } = [];
    public List<ParsedSqlBatch> ParsedBatches { get; } = [];
    public List<ParseProblem> Problems { get; } = [];
    public List<LintFinding> LintFindings { get; } = [];
    public bool IsPartial { get; set; }
    public string? PartialReason { get; set; }

    internal void AppendDependencyContribution(
        DependencyCollectionResult contribution,
        string text,
        int startLine,
        string fullDocumentText,
        string? repositoryRoot)
    {
        Objects.AddRange(contribution.Objects);
        Dependencies.AddRange(contribution.Dependencies);
        ColumnUsage.AddRange(contribution.ColumnUsage);
        Parameters.AddRange(contribution.Parameters);
        ResultColumns.AddRange(contribution.ResultColumns);
        TableKeys.AddRange(contribution.TableKeys);
        IndexColumns.AddRange(contribution.IndexColumns);
        ForeignKeys.AddRange(contribution.ForeignKeys);
        TableColumns.AddRange(contribution.TableColumns);
        Problems.AddRange(contribution.Problems);
        LintFindings.AddRange(LintSuppressions.Filter(
            contribution.LintFindings,
            text,
            startLine,
            fullDocumentText,
            repositoryRoot));
    }
}
