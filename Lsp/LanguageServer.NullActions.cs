using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? NotInNullAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "not-in-null")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var predicate = TextInRange(text, diagnostic.Range);

        if (predicate is null)
        {
            return null;
        }

        if (!TryRemoveNullFromNotIn(predicate, out var replacement))
        {
            return null;
        }

        return QuickFix(
            "Remove NULL from NOT IN list",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The NOT IN list must contain only safe literal values after NULL is removed.");
    }

    private static bool TryRemoveNullFromNotIn(string predicate, out string replacement)
    {
        replacement = "";
        var match = NotInLiteralListPattern().Match(predicate);

        if (!match.Success)
        {
            return false;
        }

        var values = match.Groups["values"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!HasSafeNotInValues(values))
        {
            return false;
        }

        var remaining = values
            .Where(value => !string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!HasRemovableNull(values, remaining))
        {
            return false;
        }

        replacement = match.Groups["prefix"].Value
            + string.Join(", ", remaining)
            + match.Groups["suffix"].Value;

        return true;
    }

    private static bool HasSafeNotInValues(string[] values)
    {
        if (values.Length < 2)
        {
            return false;
        }

        return values.All(IsSafeNotInLiteral);
    }

    private static bool IsSafeNotInLiteral(string value)
    {
        return SafeNotInLiteralPattern().IsMatch(value);
    }

    private static bool HasRemovableNull(string[] values, string[] remaining)
    {
        return remaining.Length > 0 && remaining.Length < values.Length;
    }

    [GeneratedRegex(@"^(?<prefix>.*?\bNOT\s+IN\s*\()\s*(?<values>[^()]*)\s*(?<suffix>\))$", RegexOptions.IgnoreCase)]
    private static partial Regex NotInLiteralListPattern();

    [GeneratedRegex(@"^(?:NULL|[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?|N?'(?:[^']|'')*')$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeNotInLiteralPattern();
}
