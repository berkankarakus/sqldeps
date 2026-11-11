using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
namespace SqlDeps.Analysis;

/// <summary>Analyzes one in-memory SQL document without requiring it to be saved to disk.</summary>
internal static class DocumentAnalysis
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

    private static void AnalyzeBatch(
        SqlBatch batch,
        string sourceFile,
        string defaultSchema,
        TSql160Parser parser,
        AnalysisResult result,
        string fullDocumentText,
        string? repositoryRoot)
    {
        if (batch.Text.Length > MaxBatchCharacters)
        {
            result.Problems.Add(new ParseProblem(
                sourceFile,
                batch.StartLine,
                $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."
            ));

            return;
        }

        var parsed = ParseBatch(batch.Text, parser);
        AddParseProblems(batch, sourceFile, parsed.Errors, result);

        if (parsed.Errors.Count > 0)
        {
            IndexValidPrefix(batch, sourceFile, defaultSchema, parsed.Errors[0].Line, parser, result, fullDocumentText, repositoryRoot);

            return;
        }

        if (parsed.Fragment is TSqlScript script)
        {
            result.ParsedBatches.Add(new(script, batch.Text, batch.StartLine));
            DocumentAnalysisCollector.Append(
                script,
                sourceFile,
                batch.Text,
                batch.StartLine,
                defaultSchema,
                result,
                fullDocumentText,
                repositoryRoot);
        }
    }

    private static (TSqlFragment Fragment, IList<ParseError> Errors) ParseBatch(
        string text,
        TSql160Parser parser)
    {
        using StringReader reader = new(text);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        return (fragment, errors);
    }

    private static void AddParseProblems(
        SqlBatch batch,
        string sourceFile,
        IEnumerable<ParseError> errors,
        AnalysisResult result)
    {
        foreach (var error in errors)
        {
            result.Problems.Add(new ParseProblem(
                sourceFile,
                batch.StartLine + error.Line - 1,
                error.Message,
                error.Column));
        }
    }

    private static void IndexValidPrefix(
        SqlBatch batch,
        string sourceFile,
        string defaultSchema,
        int errorLine,
        TSql160Parser parser,
        AnalysisResult result,
        string fullDocumentText,
        string? repositoryRoot
    )
    {
        var prefix = string.Join('\n', batch.Text.Split('\n').Take(errorLine - 1));
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        using StringReader reader = new(prefix);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        if (errors.Count == 0 && fragment is TSqlScript script)
        {
            DocumentAnalysisCollector.Append(
                script,
                sourceFile,
                prefix,
                batch.StartLine,
                defaultSchema,
                result,
                fullDocumentText,
                repositoryRoot);
        }
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
