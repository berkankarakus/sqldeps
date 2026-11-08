using System.Text.RegularExpressions;
using SqlDeps.Core;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? DynamicSqlParameterAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (!IsDynamicSqlDiagnostic(diagnostic))
        {
            return null;
        }

        var expressionStart = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
        if (!TryCreateDynamicSqlEdits(text, diagnostic.Range, expressionStart, out var edits))
        {
            return null;
        }

        return QuickFixEdits(
            "Convert dynamic SQL to sp_executesql parameters",
            uri,
            edits,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "Only a constant SQL expression with value-like concatenations can be parameterized.");
    }

    private static bool TryCreateDynamicSqlEdits(
        string text,
        LspRange expressionRange,
        int expressionStart,
        out IReadOnlyList<(LspRange Range, string NewText)> edits)
    {
        edits = [];

        if (!TryReadDynamicExpression(text, expressionRange, out var expression))
        {
            return false;
        }

        if (!DynamicSqlParameterizationService.TryParameterize(expression, text[..expressionStart], out var replacement))
        {
            return false;
        }

        if (!TryGetDynamicCallRanges(
                text,
                expressionRange,
                expressionStart,
                out var prefixRange,
                out var closingRange))
        {
            return false;
        }

        edits =
        [
            (prefixRange, "EXEC sys.sp_executesql "),
            (expressionRange, replacement),
            (closingRange, ""),
        ];

        return true;
    }

    private static bool TryReadDynamicExpression(
        string text,
        LspRange expressionRange,
        out string expression)
    {
        expression = "";

        if (!IsDocumentRange(text, expressionRange))
        {
            return false;
        }

        string? value = TextInRange(text, expressionRange);

        if (value is null)
        {
            return false;
        }

        expression = value;

        return true;
    }

    private static bool IsDynamicSqlDiagnostic(LintDiagnostic diagnostic)
    {
        return diagnostic.Rule == "dynamic-sql-concatenation";
    }

    private static bool TryGetDynamicCallRanges(
        string text,
        LspRange expressionRange,
        int expressionStart,
        out LspRange prefixRange,
        out LspRange closingRange)
    {
        prefixRange = null!;
        closingRange = null!;

        var call = DynamicExecPrefixPattern().Match(text[..expressionStart]);

        if (!call.Success)
        {
            return false;
        }

        prefixRange = new LspRange(
            PositionAt(text, call.Index).Start,
            PositionAt(text, expressionStart).Start);
        var expressionEnd = OffsetAt(text, expressionRange.End.Line, expressionRange.End.Character);

        if (expressionEnd >= text.Length)
        {
            return false;
        }

        if (text[expressionEnd] != ')')
        {
            return false;
        }

        closingRange = new LspRange(
            PositionAt(text, expressionEnd).Start,
            PositionAt(text, expressionEnd + 1).Start);

        return true;
    }

    [GeneratedRegex(@"\bEXEC(?:UTE)?\s*\(\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DynamicExecPrefixPattern();
}
