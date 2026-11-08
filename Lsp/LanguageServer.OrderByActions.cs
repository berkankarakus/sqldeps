using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? OrderByOrdinalAction(
        string uri,
        string? text,
        JsonElement parameters)
    {
        if (text is null || !TryGetSelectedOrdinal(text, parameters, out var ordinal, out var range))
        {
            return null;
        }

        var analysis = DocumentAnalysis.Analyze(uri, text);
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => FindOrdinalReplacements(text, batch, ordinal, range))
            .ToArray();

        if (candidates.Length != 1)
        {
            return null;
        }

        var replacement = candidates[0];

        return QuickFix(
            $"Replace ORDER BY {ordinal} with {replacement.Text}",
            uri,
            range,
            replacement.Text,
            text,
            references: ReferencesFor("order-by-ordinal"),
            experimental: true,
            diagnosticRule: "order-by-ordinal",
            applicability: "The selected ordinal must resolve to exactly one simple SELECT expression.");
    }

    private static bool TryGetSelectedOrdinal(
        string text,
        JsonElement parameters,
        out int ordinal,
        out LspRange range)
    {
        if (!TryGetSelectedText(text, parameters, out range, out var selectedText))
        {
            ordinal = 0;

            return false;
        }

        return int.TryParse(selectedText, out ordinal) && ordinal >= 1;
    }

    private static bool TryGetSelectedText(
        string text,
        JsonElement parameters,
        out LspRange range,
        out string selectedText)
    {
        range = default!;
        selectedText = string.Empty;

        if (!parameters.TryGetProperty("range", out var rangeElement))
        {
            return false;
        }

        if (!TryGetRange(rangeElement, out range))
        {
            return false;
        }

        if (TextInRange(text, range) is not { } value)
        {
            return false;
        }

        selectedText = value;

        return true;
    }

    private static IEnumerable<(string Text, LspRange Range)> FindOrdinalReplacements(
        string text,
        ParsedSqlBatch batch,
        int ordinal,
        LspRange selectedRange)
    {
        var batchOffset = BatchOffset(text, batch.StartLine);
        var selectedOffset = OffsetAt(text, selectedRange.Start.Line, selectedRange.Start.Character);

        foreach (var query in QuerySpecificationFinder.Find(batch.Script))
        {
            foreach (var orderByElement in query.OrderByClause?.OrderByElements ?? [])
            {
                if (!TryGetSelectedOrdinalExpression(
                    orderByElement,
                    query,
                    ordinal,
                    batchOffset,
                    selectedOffset,
                    out var select))
                {
                    continue;
                }

                if (!IsSimpleOrderByExpression(select.Expression))
                {
                    continue;
                }

                var expressionStart = batchOffset + select.Expression.StartOffset;
                var expressionEnd = expressionStart + select.Expression.FragmentLength;

                yield return (
                    text[expressionStart..expressionEnd],
                    new(PositionAt(text, expressionStart).Start, PositionAt(text, expressionEnd).Start));
            }
        }
    }

    private static bool TryGetSelectedOrdinalExpression(
        ExpressionWithSortOrder orderByElement,
        QuerySpecification query,
        int ordinal,
        int batchOffset,
        int selectedOffset,
        out SelectScalarExpression select)
    {
        select = null!;

        if (orderByElement.Expression is not IntegerLiteral literal)
        {
            return false;
        }

        if (!int.TryParse(literal.Value, out var position))
        {
            return false;
        }

        if (position != ordinal)
        {
            return false;
        }

        if (batchOffset + literal.StartOffset != selectedOffset)
        {
            return false;
        }

        if (position > query.SelectElements.Count)
        {
            return false;
        }

        if (query.SelectElements[position - 1] is not SelectScalarExpression selectedExpression)
        {
            return false;
        }

        select = selectedExpression;

        return true;
    }

    private static bool IsSimpleOrderByExpression(ScalarExpression expression)
    {
        return expression is ColumnReferenceExpression or FunctionCall;
    }
}
