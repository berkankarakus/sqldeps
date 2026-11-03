using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

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

    public static AnalysisResult Analyze(string sourceFile, string text, string defaultSchema = ObjectNames.StandardDefaultSchema)
    {
        AnalysisResult result = new(sourceFile, text);
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        foreach (var batch in BatchSplitter.Split(text))
        {
            AnalyzeBatch(batch, sourceFile, defaultSchema, parser, result);
        }

        return result;
    }

    private static void AnalyzeBatch(
        SqlBatch batch,
        string sourceFile,
        string defaultSchema,
        TSql160Parser parser,
        AnalysisResult result)
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
            IndexValidPrefix(batch, sourceFile, defaultSchema, parsed.Errors[0].Line, parser, result);

            return;
        }

        if (parsed.Fragment is TSqlScript script)
        {
            result.ParsedBatches.Add(new(script, batch.Text, batch.StartLine));
            Collect(script, sourceFile, batch.Text, batch.StartLine, defaultSchema, result);
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
        AnalysisResult result
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
            Collect(script, sourceFile, prefix, batch.StartLine, defaultSchema, result);
        }
    }

    private static void Collect(TSqlScript script, string sourceFile, string text, int startLine, string defaultSchema, AnalysisResult result)
    {
        DependencyCollector collector = new(sourceFile, startLine, CteNameCollector.Collect(script), defaultSchema, text);
        collector.Collect(script);
        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.Parameters.AddRange(collector.Parameters);
        result.ResultColumns.AddRange(collector.ResultColumns);
        result.TableKeys.AddRange(collector.TableKeys);
        result.IndexColumns.AddRange(collector.IndexColumns);
        result.ForeignKeys.AddRange(collector.ForeignKeys);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(collector.LintFindings, text, startLine));
        result.LocalColumns.AddRange(LocalColumnCollector.Collect(script, startLine));
    }
}

/// <summary>Collects explicit output columns of CTEs and derived tables for local LSP completion.</summary>
internal sealed partial class LocalColumnCollector(int batchStartLine) : TSqlFragmentVisitor
{
    private readonly List<LocalColumn> _columns = [];
    private int _scopeLine;

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

    public override void Visit(ProcedureStatementBody node)
    {
        var previousScope = _scopeLine;
        _scopeLine = batchStartLine + node.StartLine - 1;

        try
        {
            base.Visit(node);
        }
        finally
        {
            _scopeLine = previousScope;
        }
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (node.Alias is not null)
        {
            var columns = node.Columns.Count > 0
                ? node.Columns.Select(column => column.Value)
                : OutputColumns(node.QueryExpression);

            Add(node.Alias.Value, columns, "derived column", node);
        }
    }

    public override void Visit(DeclareTableVariableStatement node)
    {
        Add(node.Body.VariableName.Value, node.Body.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value), "table variable", node);
    }

    public override void Visit(CreateTableStatement node)
    {
        if (!ObjectNames.IsTemporary(node.SchemaObjectName))
        {
            return;
        }

        Add(
            node.SchemaObjectName.BaseIdentifier.Value,
            node.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value),
            "temporary table",
            node);
    }

    public override void Visit(AlterTableAddTableElementStatement node)
    {
        if (!ObjectNames.IsTemporary(node.SchemaObjectName))
        {
            return;
        }

        Add(
            node.SchemaObjectName.BaseIdentifier.Value,
            node.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value),
            "temporary table",
            node);
    }

    public override void Visit(DropTableStatement node)
    {
        foreach (var table in node.Objects.Where(table => ObjectNames.IsTemporary(table)))
        {
            _columns.Add(new LocalColumn(
                table.BaseIdentifier.Value,
                string.Empty,
                "temporary table drop",
                batchStartLine + node.StartLine - 1,
                IsDrop: true));
        }
    }

    public override void Visit(VariableTableReference node)
    {
        if (node.Alias is null)
        {
            return;
        }

        var columns = _columns
            .Where(column => string.Equals(column.Alias, node.Variable.Name, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.Name)
            .ToArray();

        Add(node.Alias.Value, columns, "table variable", node);
    }

    private void Add(string alias, IEnumerable<string> names, string kind, TSqlFragment node)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _columns.Add(new LocalColumn(alias, name, kind, batchStartLine + node.StartLine - 1, ScopeLine: _scopeLine));
        }
    }

    private IEnumerable<string> OutputColumns(QueryExpression query)
    {
        if (query is BinaryQueryExpression binary)
        {
            return OutputColumns(binary.FirstQueryExpression);
        }

        if (query is QueryParenthesisExpression parenthesized)
        {
            return OutputColumns(parenthesized.QueryExpression);
        }

        if (query is not QuerySpecification specification)
        {
            return [];
        }

        return specification.SelectElements.SelectMany(element => OutputColumns(element, specification));
    }

    private IEnumerable<string> OutputColumns(SelectElement element, QuerySpecification specification)
    {
        if (element is SelectScalarExpression scalar)
        {
            return ColumnName(scalar) is { } name ? [name] : [];
        }

        if (element is not SelectStarExpression star)
        {
            return [];
        }

        var qualifier = star.Qualifier?.Identifiers.LastOrDefault()?.Value;
        return ColumnsFromSources(specification.FromClause, qualifier);
    }

    private static bool MatchesQualifier(string? qualifier, string? alias, string name)
    {
        return qualifier is null
            || string.Equals(qualifier, alias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(qualifier, name, StringComparison.OrdinalIgnoreCase);
    }

    private List<string> LocalColumnsFor(string alias)
    {
        return _columns
            .Where(column => string.Equals(column.Alias, alias, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
}

internal sealed record LocalColumn(string Alias, string Name, string Kind, int Line, bool IsDrop = false, int ScopeLine = 0);
