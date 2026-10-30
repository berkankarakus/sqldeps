using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Analyzes one in-memory SQL document without requiring it to be saved to disk.</summary>
internal static class DocumentAnalysis
{
    private const int MaxBatchCharacters = 16 * 1024 * 1024;

    public static AnalysisResult Analyze(string sourceFile, string text, string defaultSchema = ObjectNames.StandardDefaultSchema)
    {
        AnalysisResult result = new(sourceFile, text);
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        foreach (var batch in BatchSplitter.Split(text))
        {
            if (batch.Text.Length > MaxBatchCharacters)
            {
                result.Problems.Add(new ParseProblem(
                    sourceFile,
                    batch.StartLine,
                    $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."
                ));

                continue;
            }

            TSqlFragment fragment;
            IList<ParseError> errors;

            using (StringReader reader = new(batch.Text))
            {
                fragment = parser.Parse(reader, out errors);
            }

            foreach (var error in errors)
            {
                result.Problems.Add(new ParseProblem(sourceFile, batch.StartLine + error.Line - 1, error.Message, error.Column));
            }

            if (errors.Count > 0)
            {
                IndexValidPrefix(batch, sourceFile, defaultSchema, errors[0].Line, parser, result);
                continue;
            }

            if (fragment is TSqlScript script)
            {
                result.ParsedBatches.Add(new(script, batch.Text, batch.StartLine));
                Collect(script, sourceFile, batch.Text, batch.StartLine, defaultSchema, result);
            }
        }

        return result;
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
            _columns.Add(new LocalColumn(alias, name, kind, batchStartLine + node.StartLine - 1));
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

    private IEnumerable<string> ColumnsFromSources(FromClause? fromClause, string? qualifier)
    {
        foreach (var table in TableReferenceFinder.Find(fromClause))
        {
            var tableName = table.SchemaObject.BaseIdentifier.Value;
            var alias = table.Alias?.Value;
            if (!MatchesQualifier(qualifier, alias, tableName))
            {
                continue;
            }

            var columns = LocalColumnsFor(alias ?? tableName);
            if (columns.Count > 0)
            {
                return columns;
            }
        }

        foreach (var table in VariableTableReferenceFinder.Find(fromClause))
        {
            var variableName = table.Variable.Name;
            var alias = table.Alias?.Value;
            if (!MatchesQualifier(qualifier, alias, variableName))
            {
                continue;
            }

            var columns = LocalColumnsFor(alias ?? variableName);
            if (columns.Count > 0)
            {
                return columns;
            }
        }

        foreach (var table in QueryDerivedTableFinder.Find(fromClause))
        {
            var alias = table.Alias?.Value;
            if (alias is null || qualifier is not null && !string.Equals(qualifier, alias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return OutputColumns(table.QueryExpression).ToArray();
        }

        return [];
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
    public List<TableColumn> TableColumns { get; } = [];
    public List<LocalColumn> LocalColumns { get; } = [];
    public List<ParsedSqlBatch> ParsedBatches { get; } = [];
    public List<ParseProblem> Problems { get; } = [];
    public List<LintFinding> LintFindings { get; } = [];
}

internal sealed record LocalColumn(string Alias, string Name, string Kind, int Line);
