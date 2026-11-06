using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

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
        var placeholder = InsertPlaceholder(statement, columnName) ?? "NULL";
        if (!TryCreateInsertPlaceholderEdits(text, batch, statement, columnName, placeholder, out var edits, out var placeholderCount))
        {
            return null;
        }

        var title = InsertPlaceholderTitle(columnName, placeholderCount);

        return QuickFixEdits(
            title,
            uri,
            edits,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The INSERT shape must be known and every generated placeholder must remain parse-valid.");
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
        string placeholder,
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
            return TryAddValuesPlaceholders(text, documentBatchOffset, statement, values, placeholder, edits, out placeholderCount);
        }

        if (statement.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification query })
        {
            return TryAddSelectPlaceholder(text, documentBatchOffset, statement, query, placeholder, edits, out placeholderCount);
        }

        return false;
    }

    private static bool TryAddValuesPlaceholders(
        string text,
        int documentBatchOffset,
        InsertStatement statement,
        ValuesInsertSource values,
        string placeholder,
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
                    placeholder));

                continue;
            }

            var lastValue = row.ColumnValues[^1];
            edits.Add((
                PositionAt(text, documentBatchOffset + lastValue.StartOffset + lastValue.FragmentLength),
                ", " + placeholder));
        }

        return true;
    }

    private static bool TryAddSelectPlaceholder(
        string text,
        int documentBatchOffset,
        InsertStatement statement,
        QuerySpecification query,
        string placeholder,
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
            ", " + placeholder));

        return true;
    }

    private string? InsertPlaceholder(InsertStatement statement, string columnName)
    {
        if (statement.InsertSpecification.Target is not NamedTableReference target)
        {
            return null;
        }

        var tableName = ObjectNames.Format(target.SchemaObject, ObjectNames.StandardDefaultSchema);
        var dataTypes = AllTableColumns()
            .Where(column => SameName(column.TableName, tableName)
                && SameName(column.ColumnName, columnName))
            .Select(column => column.DataType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (dataTypes.Length != 1 || !SafeInsertPlaceholderTypePattern().IsMatch(dataTypes[0]))
        {
            return null;
        }

        return $"CAST(NULL AS {dataTypes[0]})";
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

    [GeneratedRegex(@"^(?:BIGINT|BINARY|BIT|CHAR|DATE|DATETIME|DATETIME2|DATETIMEOFFSET|DECIMAL|FLOAT|IMAGE|INT|MONEY|NCHAR|NTEXT|NUMERIC|NVARCHAR|REAL|SMALLDATETIME|SMALLINT|SMALLMONEY|TEXT|TIME|TINYINT|UNIQUEIDENTIFIER|VARBINARY|VARCHAR|XML)(?:\s*\([^)]*\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeInsertPlaceholderTypePattern();
}
