using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class DocumentAnalysis
{
    private static void AnalyzeBatch(
        SqlBatch batch,
        string sourceFile,
        string defaultSchema,
        string? defaultDatabase,
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
            IndexValidPrefix(batch, sourceFile, defaultSchema, defaultDatabase, parsed.Errors[0].Line, parser, result, fullDocumentText, repositoryRoot);

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
                defaultDatabase,
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
        string? defaultDatabase,
        int errorLine,
        TSql160Parser parser,
        AnalysisResult result,
        string fullDocumentText,
        string? repositoryRoot)
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
                defaultDatabase,
                result,
                fullDocumentText,
                repositoryRoot);
        }
    }
}
