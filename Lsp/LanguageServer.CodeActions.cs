using System.Text.Json;
using System.Text.RegularExpressions;
using SqlDeps.Domain;

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
            if (diagnostic.IsStale || diagnostic.Source == "stale-index")
            {
                actions.Add(ReindexAction());
                continue;
            }
            else
            {
                if (SuppressLintAction(uri, document?.Text, diagnostic) is { } suppressionAction)
                {
                    actions.Add(suppressionAction);
                }
            }

            AddReplacementActions(actions, uri, document?.Text, diagnostic);
        }

        return actions;
    }

    private object ReindexAction() => new
    {
        title = "Reindex workspace (sqldeps reindex)",
        kind = "source",
        data = DiagnosticData(DiagnosticMetadataFor("stale-index")),
        command = new
        {
            title = "sqldeps reindex",
            command = "sqldeps.reindex",
            arguments = new[] { workspaceRoot },
        },
    };

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

        return QuickFix("Cast filter literal to column type", uri, diagnostic.Range, replacement, text, diagnostic.Confidence);
    }

    private static object? PagingLiteralAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule is not ("negative-offset" or "invalid-fetch"))
        {
            return null;
        }

        var literal = TextInRange(text, diagnostic.Range);
        if (!int.TryParse(literal, out var value))
        {
            return null;
        }

        if (diagnostic.Rule == "negative-offset" && value >= 0
            || diagnostic.Rule == "invalid-fetch" && value > 0)
        {
            return null;
        }

        var replacement = diagnostic.Rule == "negative-offset" ? "0" : "1";
        var title = diagnostic.Rule == "negative-offset"
            ? "Set OFFSET to 0"
            : "Set FETCH count to 1";

        return QuickFix(title, uri, diagnostic.Range, replacement, text, diagnostic.Confidence);
    }

    private static object? MissingProcedureParameterAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule != "missing-procedure-parameter")
        {
            return null;
        }

        var call = TextInRange(text, diagnostic.Range);
        var match = RequiredProcedureParameterPattern().Match(diagnostic.Message);
        if (call is null || !match.Success || !call.Contains("EXEC", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lines = text.Split('\n');
        if (diagnostic.Range.End.Line < 0 || diagnostic.Range.End.Line >= lines.Length)
        {
            return null;
        }

        var line = diagnostic.Range.End.Line;
        var lineText = lines[line].TrimEnd('\r');
        var end = Math.Min(diagnostic.Range.End.Character, lineText.Length);
        while (end > diagnostic.Range.Start.Character && char.IsWhiteSpace(lineText[end - 1]))
        {
            end--;
        }

        if (end > 0 && lineText[end - 1] == ';')
        {
            end--;
        }

        var callBody = call.Trim();
        var hasArgument = callBody.Count(character => character == ' ') >= 2;
        var separator = hasArgument ? ", " : " ";
        var parameter = match.Groups["parameter"].Value;
        var insertionRange = new LspRange(new(line, end), new(line, end));

        return QuickFix(
            $"Add required parameter '{parameter}' placeholder",
            uri,
            insertionRange,
            $"{separator}{parameter} = NULL",
            text,
            diagnostic.Confidence);
    }

    private static object? WhereTemplateAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("update-without-where" or "delete-without-where"))
        {
            return null;
        }

        var insertionPoint = diagnostic.Range.End;
        if (text is not null && insertionPoint.Line >= 0)
        {
            var lines = text.Split('\n');
            if (insertionPoint.Line < lines.Length)
            {
                var line = lines[insertionPoint.Line].TrimEnd('\r');
                var end = Math.Min(insertionPoint.Character, line.Length);
                while (end > 0 && char.IsWhiteSpace(line[end - 1]))
                {
                    end--;
                }

                if (end > 0 && line[end - 1] == ';')
                {
                    insertionPoint = new(insertionPoint.Line, end - 1);
                }
            }
        }

        var insertionRange = new LspRange(insertionPoint, insertionPoint);

        return QuickFix("Insert safe WHERE 1 = 0 template", uri, insertionRange, " WHERE 1 = 0", text, diagnostic.Confidence);
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

        var statementEnd = text.IndexOf(';', match.Index + match.Length);
        var statementTail = text[(match.Index + match.Length)..(statementEnd < 0 ? text.Length : statementEnd)];
        if (Regex.IsMatch(statementTail, @"\b(?:JOIN|APPLY)\b", RegexOptions.IgnoreCase))
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

        return QuickFix("Expand SELECT * with indexed columns", uri, diagnostic.Range, string.Join(", ", columns), text, diagnostic.Confidence);
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

        return QuickFix($"Replace with '{replacement}'", uri, diagnostic.Range, replacement, text, diagnostic.Confidence);
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

    private static object? QuickFixEdits(
        string title,
        string uri,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        string? text,
        string diagnosticConfidence
    )
    {
        if (text is null || !TryOrderQuickFixEdits(text, edits, out var ordered))
        {
            return null;
        }

        var editedText = ApplyQuickFixEdits(text, ordered);
        if (IntroducesParseProblems(uri, text, editedText, edits[0].Range.Start.Line))
        {
            return null;
        }

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(diagnosticConfidence, edits),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [uri] = edits.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray(),
                },
            },
        };
    }

    private static bool TryOrderQuickFixEdits(
        string text,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        out (LspRange Range, string NewText, int Start, int End)[] ordered)
    {
        ordered = [];

        if (edits.Count == 0 || edits.Any(edit => !IsDocumentRange(text, edit.Range)))
        {
            return false;
        }

        ordered = edits
            .Select(edit => (
                edit.Range,
                edit.NewText,
                Start: OffsetAt(text, edit.Range.Start.Line, edit.Range.Start.Character),
                End: OffsetAt(text, edit.Range.End.Line, edit.Range.End.Character)))
            .OrderByDescending(edit => edit.Start)
            .ToArray();

        if (ordered.Any(edit => edit.End < edit.Start)
            || ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Start < pair.Second.End))
        {
            ordered = [];

            return false;
        }

        return true;
    }

    private static string ApplyQuickFixEdits(
        string text,
        IEnumerable<(LspRange Range, string NewText, int Start, int End)> ordered)
    {
        var editedText = text;

        foreach (var edit in ordered)
        {
            editedText = editedText[..edit.Start] + edit.NewText + editedText[edit.End..];
        }

        return editedText;
    }

    private static bool IntroducesParseProblems(
        string uri,
        string originalText,
        string editedText,
        int changedLine)
    {
        var beforeProblems = DocumentAnalysis.ParseProblemCountForBatch(uri, originalText, changedLine);
        var afterProblems = DocumentAnalysis.ParseProblemCountForBatch(uri, editedText, changedLine);

        return afterProblems > beforeProblems;
    }

    private static object ReplacementActionData(
        string diagnosticConfidence,
        IReadOnlyList<(LspRange Range, string NewText)> edits
    )
    {
        var metadata = DiagnosticMetadataMapper.For("sqldeps");

        return new
        {
            source = metadata.Source,
            confidence = metadata.Confidence,
            diagnosticConfidence,
            stale = metadata.IsStale,
            diff = edits.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray(),
        };
    }

    private static LspRange PositionAt(string text, int offset)
    {
        var bounded = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < bounded; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        var character = bounded - lineStart;
        return new(new(line, character), new(line, character));
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

    [GeneratedRegex(@"\bSELECT\s+(?<star>\*)\s+FROM\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?:\s+(?:AS\s+)?\w+)?(?=\s*(?:WHERE|GROUP|ORDER|JOIN|;|$))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectStarPattern();

    [GeneratedRegex(@"String literal .* is compared with '(?<type>[^']+)' column", RegexOptions.IgnoreCase)]
    private static partial Regex ImplicitFilterConversionPattern();

    [GeneratedRegex(@"requires parameter '(?<parameter>@[A-Za-z0-9_]+)'", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredProcedureParameterPattern();

    [GeneratedRegex(@"INSERT\s+omits\s+required\s+column\s+'(?<column>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex MissingInsertColumnPattern();

    [GeneratedRegex(@"Invalid column name '(?<column>[^']+)' on '(?<table>[^']+)'\.", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownColumnPattern();
}
