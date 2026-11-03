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

        var analysis = AnalyzeDocument(uri, text);
        var targetLine = diagnostic.Range.Start.Line + 1;
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => InsertStatements(batch.Script)
                .Select(statement => (batch, statement)))
            .Where(candidate => candidate.batch.StartLine + candidate.statement.StartLine - 1 == targetLine)
            .ToArray();

        if (candidates.Length != 1)
        {
            return null;
        }

        var (batch, statement) = candidates[0];
        if (statement.InsertSpecification.Columns.Count == 0)
        {
            return null;
        }

        var columnName = column.Groups["column"].Value;
        var documentBatchOffset = BatchOffset(text, batch.StartLine);
        var lastColumn = statement.InsertSpecification.Columns[^1];
        List<(LspRange Range, string NewText)> edits =
        [
            (
                PositionAt(text, documentBatchOffset + lastColumn.StartOffset + lastColumn.FragmentLength),
                ", " + columnName)
        ];

        if (statement.InsertSpecification.InsertSource is ValuesInsertSource values)
        {
            if (values.RowValues.Count == 0
                || values.RowValues.Any(row => row.ColumnValues.Count != statement.InsertSpecification.Columns.Count))
            {
                return null;
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
        }
        else if (statement.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification query }
            && query.SelectElements.Count == statement.InsertSpecification.Columns.Count
            && query.SelectElements.All(element => element is not SelectStarExpression))
        {
            var lastElement = query.SelectElements[^1];
            edits.Add((
                PositionAt(text, documentBatchOffset + lastElement.StartOffset + lastElement.FragmentLength),
                ", NULL"));
        }
        else
        {
            return null;
        }

        var placeholderCount = statement.InsertSpecification.InsertSource is ValuesInsertSource valueSource
            ? valueSource.RowValues.Count
            : 1;
        var title = placeholderCount == 1
            ? $"Add required INSERT column '{columnName}' placeholder"
            : $"Add required INSERT column '{columnName}' placeholders";
        return QuickFixEdits(title, uri, edits, text, diagnostic.Confidence);
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
