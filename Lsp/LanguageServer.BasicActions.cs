using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? NullComparisonAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "null-comparison")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var comparison = TextInRange(text, diagnostic.Range);

        if (comparison is null)
        {
            return null;
        }

        if (!TryNullComparisonReplacement(comparison, out var replacement))
        {
            return null;
        }

        return QuickFix(
            $"Replace with {replacement}",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The NULL comparison must be rewritten without changing the intended predicate semantics.");
    }

    private object? AliasReplacementAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "table-name-after-alias")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var reference = TextInRange(text, diagnostic.Range);

        if (reference is null)
        {
            return null;
        }

        if (!TryAliasReplacement(reference, diagnostic.Message, out var replacement, out var alias))
        {
            return null;
        }

        return QuickFix(
            $"Replace table name with alias '{alias}'",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The reference must use the alias declared for the source table in this query scope.");
    }

    private IEnumerable<object> AmbiguousColumnActions(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "ambiguous-column" || text is null)
        {
            return [];
        }

        var candidates = AmbiguousColumnCandidates(diagnostic.Message);

        if (candidates.Count == 0)
        {
            return [];
        }

        var column = TextInRange(text, diagnostic.Range);

        if (column is null)
        {
            return [];
        }

        return candidates
            .Select(candidate => QuickFix(
                $"Qualify with '{candidate}'",
                uri,
                diagnostic.Range,
                $"{candidate}.{column}",
                text,
                diagnostic.Confidence,
                diagnosticRule: diagnostic.Rule,
                applicability: "The selected table alias must uniquely own the ambiguous column."))
            .OfType<object>();
    }

    private static IReadOnlyList<string> AmbiguousColumnCandidates(string message)
    {
        var match = AmbiguousColumnCandidatesPattern().Match(message);

        return match.Success ? match.Groups["candidates"].Value.Split(", ") : [];
    }

    private object? QuickFix(
        string title,
        string uri,
        LspRange range,
        string newText,
        string? text,
        string diagnosticConfidence = "high",
        IReadOnlyList<DiagnosticReference>? references = null,
        bool experimental = false,
        string? diagnosticRule = null,
        string? applicability = null,
        bool includeRefactorCandidate = true,
        string? suppressionScope = null
    )
    {
        if (text is null)
        {
            return null;
        }

        if (!IsDocumentRange(text, range))
        {
            return null;
        }

        if (!TryValidateRewrite(text, [(range, newText)], range.Start.Line, out _))
        {
            return null;
        }

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(
                title,
                uri,
                diagnosticConfidence,
                [(range, newText)],
                references,
                experimental,
                diagnosticRule,
                applicability,
                includeRefactorCandidate,
                suppressionScope),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [uri] = new[] { new { range, newText } }
                }
            }
        };
    }

    private static bool TryAliasReplacement(string reference, string message, out string replacement, out string alias)
    {
        var aliasMatch = AliasInDiagnosticPattern().Match(message);
        var separator = reference.LastIndexOf('.');

        if (IsInvalidAliasReplacement(aliasMatch, separator, reference.Length))
        {
            replacement = "";
            alias = "";
            return false;
        }

        alias = aliasMatch.Groups["alias"].Value;
        replacement = alias + reference[separator..];

        return true;
    }

    private static bool IsInvalidAliasReplacement(Match aliasMatch, int separator, int referenceLength)
    {
        return !aliasMatch.Success
            || separator <= 0
            || separator == referenceLength - 1;
    }

    private static string? TextInRange(string text, LspRange range)
    {
        if (range.Start.Line != range.End.Line || range.Start.Line < 0)
        {
            return null;
        }

        var lines = text.Split('\n');

        if (range.Start.Line >= lines.Length)
        {
            return null;
        }

        var line = lines[range.Start.Line].TrimEnd('\r');
        var start = range.Start.Character;
        var length = range.End.Character - start;

        return start < 0 || length < 0 || start + length > line.Length ? null : line.Substring(start, length);
    }

    private static bool TryNullComparisonReplacement(string comparison, out string replacement)
    {
        var match = NullComparisonPattern().Match(comparison);

        if (!match.Success)
        {
            replacement = "";
            return false;
        }

        var operatorText = match.Groups["operator"].Success
            ? match.Groups["operator"].Value
            : match.Groups["reverseOperator"].Value;

        var value = match.Groups["value"].Success
            ? match.Groups["value"].Value
            : match.Groups["reverseValue"].Value;

        replacement = $"{value.Trim()} IS{(operatorText == "=" ? "" : " NOT")} NULL";

        return true;
    }
}
