using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Analyzes one in-memory SQL document without requiring it to be saved to disk.</summary>
internal static class DocumentAnalysis
{
    public static AnalysisResult Analyze(string sourceFile, string text, string defaultSchema = ObjectNames.StandardDefaultSchema)
    {
        AnalysisResult result = new(sourceFile, text);

        foreach (var batch in BatchSplitter.Split(text))
        {
            TSql160Parser parser = new(initialQuotedIdentifiers: true);
            TSqlFragment fragment;
            IList<ParseError> errors;

            using (StringReader reader = new(batch.Text))
            {
                fragment = parser.Parse(reader, out errors);
            }

            foreach (var error in errors)
            {
                result.Problems.Add(new ParseProblem(sourceFile, batch.StartLine + error.Line - 1, error.Message));
            }

            if (errors.Count > 0 || fragment is not TSqlScript script)
            {
                continue;
            }

            DependencyCollector collector = new(sourceFile, batch.StartLine, CteNameCollector.Collect(script), defaultSchema);
            collector.Collect(script);
            result.Objects.AddRange(collector.Objects);
            result.Dependencies.AddRange(collector.Dependencies);
            result.Parameters.AddRange(collector.Parameters);
            result.TableColumns.AddRange(collector.TableColumns);
            result.Problems.AddRange(collector.Problems);
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
    public List<ObjectParameter> Parameters { get; } = [];
    public List<TableColumn> TableColumns { get; } = [];
    public List<ParseProblem> Problems { get; } = [];
}
