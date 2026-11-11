using System.Text.Json;
using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static IEnumerable<LintDiagnostic> LintDiagnostics(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("context", out var context))
        {
            return [];
        }

        if (!context.TryGetProperty("diagnostics", out var diagnostics))
        {
            return [];
        }

        return diagnostics.EnumerateArray().Select(TryLintDiagnostic).OfType<LintDiagnostic>();
    }

    private static LintDiagnostic? TryLintDiagnostic(JsonElement diagnostic)
    {
        if (!IsSqldepsDiagnostic(diagnostic))
        {
            return null;
        }

        if (!TryGetStringProperty(diagnostic, "code", out var rule))
        {
            return null;
        }

        if (!diagnostic.TryGetProperty("range", out var range))
        {
            return null;
        }

        if (!TryGetRange(range, out var diagnosticRange))
        {
            return null;
        }

        var message = diagnostic.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? ""
            : "";
        var source = diagnostic.TryGetProperty("data", out var data)
            && data.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString() ?? ""
            : "";
        var confidence = diagnostic.TryGetProperty("data", out data)
            && data.TryGetProperty("confidence", out var confidenceElement)
            ? confidenceElement.GetString() ?? "high"
            : "high";
        var stale = source == "stale-index"
            || diagnostic.TryGetProperty("data", out data)
            && data.TryGetProperty("stale", out var staleElement)
            && staleElement.ValueKind == JsonValueKind.True;

        return new LintDiagnostic(rule, diagnosticRange, message, source, confidence, stale);
    }

    private static bool IsSqldepsDiagnostic(JsonElement diagnostic)
    {
        if (!TryGetStringProperty(diagnostic, "source", out var source))
        {
            return false;
        }

        return string.Equals(source, "sqldeps", StringComparison.Ordinal);
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = "";

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";

        return true;
    }

    private static bool TryGetRange(JsonElement range, out LspRange result)
    {
        result = default!;

        if (!range.TryGetProperty("start", out var start))
        {
            return false;
        }

        if (!TryGetPosition(start, out var startPosition))
        {
            return false;
        }

        if (!range.TryGetProperty("end", out var end))
        {
            return false;
        }

        if (!TryGetPosition(end, out var endPosition))
        {
            return false;
        }

        result = new(startPosition, endPosition);

        return true;
    }

    private static bool TryGetPosition(JsonElement position, out LspPosition result)
    {
        result = default!;

        if (!TryGetInt32Property(position, "line", out var line))
        {
            return false;
        }

        if (!TryGetInt32Property(position, "character", out var character))
        {
            return false;
        }

        result = new(line, character);

        return true;
    }

    private static bool TryGetInt32Property(JsonElement element, string propertyName, out int value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (!property.TryGetInt32(out value))
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\bSELECT\s+(?<star>\*)\s+FROM\s+(?<table>(?:[@#]?(?:\[[^\]]+\]|\w+))(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?:\s+(?:AS\s+)?\w+)?(?=\s*(?:WHERE|GROUP|ORDER|JOIN|;|$))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectStarPattern();

    [GeneratedRegex(@"String literal .* is compared with '(?<type>[^']+)' column", RegexOptions.IgnoreCase)]
    private static partial Regex ImplicitFilterConversionPattern();

    [GeneratedRegex(@"requires parameter '(?<parameter>@[A-Za-z0-9_]+)'", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredProcedureParameterPattern();

    [GeneratedRegex(@"INSERT\s+omits\s+required\s+column\s+'(?<column>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex MissingInsertColumnPattern();

    [GeneratedRegex(@"Invalid column name '(?<column>[^']+)' on '(?<table>[^']+)'\.", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownColumnPattern();

    [GeneratedRegex(@"\bWITH\s*\(\s*NOLOCK\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockOnlyHintPattern();

    [GeneratedRegex(@"\bWITH\s*\(\s*NOLOCK\s*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockFirstHintPattern();

    [GeneratedRegex(@",\s*NOLOCK\s*(?=,|\))", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockListHintPattern();
}
