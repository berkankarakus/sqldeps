using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
namespace SqlDeps.Analysis;

/// <summary>Analyzes one in-memory SQL document without requiring it to be saved to disk.</summary>
internal static partial class DocumentAnalysis
{
    private const int MaxBatchCharacters = 16 * 1024 * 1024;

    public static int ParseProblemCountForBatch(string sourceFile, string text, int zeroBasedLine)
    {
        var batch = FindBatchAtLine(text, zeroBasedLine);

        return batch is null ? 0 : Analyze(sourceFile, batch.Text).Problems.Count;
    }

    public static AnalysisResult AnalyzeBatchAtLine(string sourceFile, string text, int zeroBasedLine)
    {
        var batch = FindBatchAtLine(text, zeroBasedLine);

        return batch is null ? new AnalysisResult(sourceFile, text) : Analyze(sourceFile, batch.Text);
    }

    private static SqlBatch? FindBatchAtLine(string text, int zeroBasedLine)
    {
        var targetLine = zeroBasedLine + 1;

        return BatchSplitter.Split(text)
            .FirstOrDefault(candidate => targetLine >= candidate.StartLine
                && targetLine <= candidate.StartLine + candidate.Text.Count(character => character == '\n'));
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
