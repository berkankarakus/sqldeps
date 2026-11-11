using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
namespace SqlDeps.Analysis;

/// <summary>Analyzes one in-memory SQL document without requiring it to be saved to disk.</summary>
internal static partial class DocumentAnalysis
{
    private const int MaxBatchCharacters = 16 * 1024 * 1024;

    public static int ParseProblemCountForBatch(string sourceFile, string text, int zeroBasedLine)
    {
        var targetLine = zeroBasedLine + 1;
        var batch = BatchSplitter.Split(text)
            .FirstOrDefault(candidate => targetLine >= candidate.StartLine
                && targetLine <= candidate.StartLine + candidate.Text.Count(character => character == '\n'));

        return batch is null ? 0 : Analyze(sourceFile, batch.Text).Problems.Count;
    }

    public static AnalysisResult AnalyzeBatchAtLine(string sourceFile, string text, int zeroBasedLine)
    {
        var targetLine = zeroBasedLine + 1;
        var batch = BatchSplitter.Split(text)
            .FirstOrDefault(candidate => targetLine >= candidate.StartLine
                && targetLine <= candidate.StartLine + candidate.Text.Count(character => character == '\n'));

        return batch is null ? new AnalysisResult(sourceFile, text) : Analyze(sourceFile, batch.Text);
    }

    public static AnalysisResult Analyze(
        string sourceFile,
        string text,
        string defaultSchema = ObjectNames.StandardDefaultSchema,
        string? repositoryRoot = null)
    {
        AnalysisResult result = new(sourceFile, text);
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        foreach (var batch in BatchSplitter.Split(text))
        {
            AnalyzeBatch(batch, sourceFile, defaultSchema, parser, result, text, repositoryRoot);
        }

        return result;
    }

    public static AnalysisResult AnalyzeLimited(
        string sourceFile,
        string text,
        int maxCharacters,
        string defaultSchema = ObjectNames.StandardDefaultSchema,
        string? repositoryRoot = null)
    {
        AnalysisResult result = new(sourceFile, text);
        bool exceedsLimit = text.Length > maxCharacters;

        if (exceedsLimit)
        {
            result.IsPartial = true;
            result.PartialReason = $"Document exceeds the {maxCharacters / (1024 * 1024)} MiB full-analysis limit.";
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        var analyzedCharacters = 0;

        foreach (var batch in BatchSplitter.Split(text))
        {
            if (analyzedCharacters > 0 && analyzedCharacters + batch.Text.Length > maxCharacters)
            {
                break;
            }

            AnalyzeBatch(batch, sourceFile, defaultSchema, parser, result, text, repositoryRoot);
            analyzedCharacters += batch.Text.Length;

            if (analyzedCharacters >= maxCharacters)
            {
                break;
            }
        }

        return result;
    }

}
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
