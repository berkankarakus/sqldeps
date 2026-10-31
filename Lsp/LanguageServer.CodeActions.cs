using System.Text.Json;
using System.Text.RegularExpressions;

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
            if (diagnostic.Source == "stale-index")
            {
                actions.Add(ReindexAction());
            }
            else
            {
                actions.Add(SuppressLintAction(uri, diagnostic));
            }

            AddReplacementActions(actions, uri, document?.Text, diagnostic);
        }

        return actions;
    }

    private object ReindexAction() => new
    {
        title = "Reindex workspace (sqldeps reindex)",
        kind = "source",
        command = new
        {
            title = "sqldeps reindex",
            command = "sqldeps.reindex",
            arguments = new[] { workspaceRoot },
        },
    };

    private void AddReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Confidence == "low")
        {
            return;
        }

        if (text is not null && !IsDocumentRange(text, diagnostic.Range))
        {
            return;
        }

        if (NullComparisonAction(uri, text, diagnostic) is { } nullAction)
        {
            actions.Add(nullAction);
        }

        if (AliasReplacementAction(uri, text, diagnostic) is { } aliasAction)
        {
            actions.Add(aliasAction);
        }

        if (SimilarColumnAction(uri, text, diagnostic) is { } suggestionAction)
        {
            actions.Add(suggestionAction);
        }

        actions.AddRange(AmbiguousColumnActions(uri, text, diagnostic));

        if (SelectStarAction(uri, text, diagnostic) is { } selectStarAction)
        {
            actions.Add(selectStarAction);
        }

        if (WhereTemplateAction(uri, diagnostic) is { } whereAction)
        {
            actions.Add(whereAction);
        }

        if (ImplicitFilterConversionAction(uri, text, diagnostic) is { } conversionAction)
        {
            actions.Add(conversionAction);
        }
    }

    private static bool IsDocumentRange(string text, LspRange range)
    {
        if (range.Start.Line < 0 || range.Start.Character < 0
            || range.End.Line < range.Start.Line
            || (range.End.Line == range.Start.Line && range.End.Character < range.Start.Character))
        {
            return false;
        }

        var lines = text.Split('\n');

        if (range.Start.Line >= lines.Length || range.End.Line >= lines.Length)
        {
            return false;
        }

        var startLineLength = lines[range.Start.Line].TrimEnd('\r').Length;
        var endLineLength = lines[range.End.Line].TrimEnd('\r').Length;

        return range.Start.Character <= startLineLength
            && range.End.Character <= endLineLength;
    }

    private static object? ImplicitFilterConversionAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "implicit-filter-conversion" || text is null)
        {
            return null;
        }

        var literal = TextInRange(text, diagnostic.Range);
        var match = ImplicitFilterConversionPattern().Match(diagnostic.Message);

        if (literal is null || !match.Success)
        {
            return null;
        }

        var type = match.Groups["type"].Value.Trim();
        var replacement = $"CAST({literal} AS {type})";

        return QuickFix("Cast filter literal to column type", uri, diagnostic.Range, replacement);
    }

    private static object? WhereTemplateAction(string uri, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("update-without-where" or "delete-without-where"))
        {
            return null;
        }

        var insertionPoint = new LspRange(diagnostic.Range.End, diagnostic.Range.End);

        return QuickFix("Insert safe WHERE 1 = 0 template", uri, insertionPoint, " WHERE 1 = 0");
    }

    private object? SelectStarAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "select-star" || text is null)
        {
            return null;
        }

        var match = SelectStarPattern().Match(text);
        var star = match.Groups["star"];

        if (!match.Success || !star.Success || star.Index != OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character))
        {
            return null;
        }

        var tableName = Qualify(match.Groups["table"].Value);
        var columns = AllTableColumns()
            .Where(column => string.Equals(column.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (columns.Length == 0)
        {
            return null;
        }

        return QuickFix("Expand SELECT * with indexed columns", uri, diagnostic.Range, string.Join(", ", columns));
    }

    private static object? SimilarColumnAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (!IsUnknownColumnDiagnostic(diagnostic) || text is null)
        {
            return null;
        }

        var suggestion = SuggestedColumn(diagnostic.Message);

        if (suggestion is null)
        {
            return null;
        }

        var reference = TextInRange(text, diagnostic.Range);

        if (reference is null)
        {
            return null;
        }

        var replacement = ReplaceLastIdentifier(reference, suggestion);

        return QuickFix($"Replace with '{replacement}'", uri, diagnostic.Range, replacement);
    }

    private static bool IsUnknownColumnDiagnostic(LintDiagnostic diagnostic)
    {
        return diagnostic.Rule is "unknown-column" or "unknown-insert-column" or "unknown-update-column";
    }

    private static string? SuggestedColumn(string message)
    {
        var match = SuggestedColumnPattern().Match(message);

        return match.Success ? match.Groups["suggestion"].Value : null;
    }

    private static string ReplaceLastIdentifier(string reference, string replacement)
    {
        var separator = reference.LastIndexOf('.');

        return separator < 0 ? replacement : reference[..(separator + 1)] + replacement;
    }

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

        return new LintDiagnostic(rule, diagnosticRange, message, source, confidence);
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

    [GeneratedRegex(@"\bSELECT\s+(?<star>\*)\s+FROM\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?:\s+(?:AS\s+)?\w+)?(?=\s*(?:WHERE|GROUP|ORDER|JOIN|;|$))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectStarPattern();

    [GeneratedRegex(@"String literal .* is compared with '(?<type>[^']+)' column", RegexOptions.IgnoreCase)]
    private static partial Regex ImplicitFilterConversionPattern();
}
