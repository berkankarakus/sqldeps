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

            if (errors.Count > 0)
            {
                IndexValidPrefix(batch, sourceFile, defaultSchema, errors[0].Line, result);
                continue;
            }

            if (fragment is TSqlScript script)
            {
                Collect(script, sourceFile, batch.Text, batch.StartLine, defaultSchema, result);
            }
        }

        return result;
    }

    private static void IndexValidPrefix(SqlBatch batch, string sourceFile, string defaultSchema, int errorLine, AnalysisResult result)
    {
        var prefix = string.Join('\n', batch.Text.Split('\n').Take(errorLine - 1));
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(prefix);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        if (errors.Count == 0 && fragment is TSqlScript script)
        {
            Collect(script, sourceFile, prefix, batch.StartLine, defaultSchema, result);
        }
    }

    private static void Collect(TSqlScript script, string sourceFile, string text, int startLine, string defaultSchema, AnalysisResult result)
    {
        DependencyCollector collector = new(sourceFile, startLine, CteNameCollector.Collect(script), defaultSchema);
        collector.Collect(script);
        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.Parameters.AddRange(collector.Parameters);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(collector.LintFindings, text, startLine));
        result.LocalColumns.AddRange(LocalColumnCollector.Collect(script, startLine));
    }
}

/// <summary>Collects explicit output columns of CTEs and derived tables for local LSP completion.</summary>
internal sealed class LocalColumnCollector(int batchStartLine) : TSqlFragmentVisitor
{
    private readonly List<LocalColumn> _columns = [];

    public static IReadOnlyList<LocalColumn> Collect(TSqlFragment fragment, int batchStartLine)
    {
        LocalColumnCollector collector = new(batchStartLine);
        fragment.Accept(collector);
        return collector._columns;
    }

    public override void Visit(CommonTableExpression node)
    {
        var names = node.Columns.Count > 0
            ? node.Columns.Select(column => column.Value)
            : OutputColumns(node.QueryExpression);
        Add(node.ExpressionName.Value, names, "CTE", node);
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (node.Alias is not null)
        {
            Add(node.Alias.Value, OutputColumns(node.QueryExpression), "derived column", node);
        }
    }

    private void Add(string alias, IEnumerable<string> names, string kind, TSqlFragment node)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _columns.Add(new LocalColumn(alias, name, kind, batchStartLine + node.StartLine - 1));
        }
    }

    private static IEnumerable<string> OutputColumns(QueryExpression query)
    {
        if (query is not QuerySpecification specification)
        {
            return [];
        }

        return specification.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(ColumnName)
            .Where(name => name is not null)
            .Select(name => name!);
    }

    private static string? ColumnName(SelectScalarExpression element)
    {
        if (element.ColumnName is not null)
        {
            return element.ColumnName.Value;
        }

        return element.Expression is ColumnReferenceExpression column
            ? column.MultiPartIdentifier.Identifiers.LastOrDefault()?.Value
            : null;
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
    public List<LocalColumn> LocalColumns { get; } = [];
    public List<ParseProblem> Problems { get; } = [];
    public List<LintFinding> LintFindings { get; } = [];
}

internal sealed record LocalColumn(string Alias, string Name, string Kind, int Line);
