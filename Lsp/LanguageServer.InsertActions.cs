using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? MissingRequiredInsertColumnAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule != "missing-required-insert-column")
        {
            return null;
        }

        var column = MissingInsertColumnPattern().Match(diagnostic.Message);
        if (!column.Success)
        {
            return null;
        }

        if (!TryFindInsertStatement(uri, text, diagnostic.Range.Start.Line, out var batch, out var statement))
        {
            return null;
        }

        if (statement.InsertSpecification.Columns.Count == 0)
        {
            return null;
        }

        var columnName = column.Groups["column"].Value;
        if (!TryCreateInsertPlaceholderEdits(text, batch, statement, columnName, out var edits, out var placeholderCount))
        {
            return null;
        }

        var title = InsertPlaceholderTitle(columnName, placeholderCount);

        return QuickFixEdits(title, uri, edits, text, diagnostic.Confidence);
    }

    private bool TryFindInsertStatement(
        string uri,
        string text,
        int zeroBasedLine,
        out ParsedSqlBatch batch,
        out InsertStatement statement)
    {
        var analysis = AnalyzeDocument(uri, text);
        var targetLine = zeroBasedLine + 1;
        var matches = analysis.ParsedBatches
            .SelectMany(candidate => InsertStatements(candidate.Script)
                .Select(insert => (Batch: candidate, Statement: insert)))
            .Where(candidate => candidate.Batch.StartLine + candidate.Statement.StartLine - 1 == targetLine)
            .ToArray();

        if (matches.Length != 1)
        {
            batch = null!;
            statement = null!;

            return false;
        }

        batch = matches[0].Batch;
        statement = matches[0].Statement;

        return true;
    }

    private static bool TryCreateInsertPlaceholderEdits(
        string text,
        ParsedSqlBatch batch,
        InsertStatement statement,
        string columnName,
        out List<(LspRange Range, string NewText)> edits,
        out int placeholderCount)
    {
        var documentBatchOffset = BatchOffset(text, batch.StartLine);
        var lastColumn = statement.InsertSpecification.Columns[^1];
        edits =
        [
            (
                PositionAt(text, documentBatchOffset + lastColumn.StartOffset + lastColumn.FragmentLength),
                ", " + columnName)
        ];
        placeholderCount = 0;

        if (statement.InsertSpecification.InsertSource is ValuesInsertSource values)
        {
            return TryAddValuesPlaceholders(text, documentBatchOffset, statement, values, edits, out placeholderCount);
        }

        if (statement.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification query })
        {
            return TryAddSelectPlaceholder(text, documentBatchOffset, statement, query, edits, out placeholderCount);
        }

        return false;
    }

    private static bool TryAddValuesPlaceholders(
        string text,
        int documentBatchOffset,
        InsertStatement statement,
        ValuesInsertSource values,
        List<(LspRange Range, string NewText)> edits,
        out int placeholderCount)
    {
        placeholderCount = values.RowValues.Count;
        if (values.RowValues.Count == 0
            || values.RowValues.Any(row => row.ColumnValues.Count != statement.InsertSpecification.Columns.Count))
        {
            return false;
        }

        foreach (var row in values.RowValues)
        {
            if (row.ColumnValues.Count == 0)
            {
                edits.Add((
                    PositionAt(text, documentBatchOffset + row.StartOffset + 1),
                    "NULL"));

                continue;
            }

            var lastValue = row.ColumnValues[^1];
            edits.Add((
                PositionAt(text, documentBatchOffset + lastValue.StartOffset + lastValue.FragmentLength),
                ", NULL"));
        }

        return true;
    }

    private static bool TryAddSelectPlaceholder(
        string text,
        int documentBatchOffset,
        InsertStatement statement,
        QuerySpecification query,
        List<(LspRange Range, string NewText)> edits,
        out int placeholderCount)
    {
        placeholderCount = 1;
        if (query.SelectElements.Count != statement.InsertSpecification.Columns.Count
            || query.SelectElements.Any(element => element is SelectStarExpression))
        {
            return false;
        }

        var lastElement = query.SelectElements[^1];
        edits.Add((
            PositionAt(text, documentBatchOffset + lastElement.StartOffset + lastElement.FragmentLength),
            ", NULL"));

        return true;
    }

    private static string InsertPlaceholderTitle(string columnName, int placeholderCount)
    {
        return placeholderCount == 1
            ? $"Add required INSERT column '{columnName}' placeholder"
            : $"Add required INSERT column '{columnName}' placeholders";
    }

    private static IEnumerable<InsertStatement> InsertStatements(TSqlFragment fragment)
    {
        InsertStatementVisitor visitor = new();
        fragment.Accept(visitor);
        return visitor.Statements;
    }

    private sealed class InsertStatementVisitor : TSqlFragmentVisitor
    {
        public List<InsertStatement> Statements { get; } = [];

        public override void Visit(InsertStatement node)
        {
            Statements.Add(node);
            base.Visit(node);
        }
    }
}
