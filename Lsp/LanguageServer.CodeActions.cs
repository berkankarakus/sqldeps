using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Handles LSP quick-fix requests and diagnostic decoding.</summary>
public sealed partial class LanguageServer
{
    private object CodeActions(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var actions = new List<object>();
        var document = OpenDocument(uri);

        foreach (var diagnostic in LintDiagnostics(parameters).Distinct())
        {
            actions.Add(SuppressLintAction(uri, diagnostic));
            AddReplacementActions(actions, uri, document?.Text, diagnostic);
        }

        return actions;
    }

    private static void AddReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        if (NullComparisonAction(uri, text, diagnostic) is { } nullAction)
        {
            actions.Add(nullAction);
        }

        if (AliasReplacementAction(uri, text, diagnostic) is { } aliasAction)
        {
            actions.Add(aliasAction);
        }

        actions.AddRange(AmbiguousColumnActions(uri, text, diagnostic));
    }

    private static IEnumerable<LintDiagnostic> LintDiagnostics(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("context", out var context)
            || !context.TryGetProperty("diagnostics", out var diagnostics))
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

        if (!diagnostic.TryGetProperty("range", out var range) || !TryGetRange(range, out var diagnosticRange))
        {
            return null;
        }

        var message = diagnostic.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? ""
            : "";

        return new LintDiagnostic(rule, diagnosticRange, message);
    }

    private static bool IsSqldepsDiagnostic(JsonElement diagnostic) => TryGetStringProperty(diagnostic, "source", out var source)
        && string.Equals(source, "sqldeps", StringComparison.Ordinal);

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = "";

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";

        return true;
    }

    private static bool TryGetRange(JsonElement range, out LspRange result)
    {
        result = default!;

        if (!range.TryGetProperty("start", out var start) || !TryGetPosition(start, out var startPosition))
        {
            return false;
        }

        if (!range.TryGetProperty("end", out var end) || !TryGetPosition(end, out var endPosition))
        {
            return false;
        }

        result = new(startPosition, endPosition);

        return true;
    }

    private static bool TryGetPosition(JsonElement position, out LspPosition result)
    {
        result = default!;

        if (!position.TryGetProperty("line", out var line)
            || !position.TryGetProperty("character", out var character)
            || !line.TryGetInt32(out var lineNumber)
            || !character.TryGetInt32(out var characterNumber))
        {
            return false;
        }

        result = new(lineNumber, characterNumber);

        return true;
    }
}
