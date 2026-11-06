using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Domain;

namespace SqlDeps.Mcp;

internal static class RefactorCandidateProjector
{
    internal static RefactorCandidate[] Project(IEnumerable<JsonElement> actions)
    {
        return Project(actions, null);
    }

    internal static RefactorCandidate[] Project(
        IEnumerable<JsonElement> actions,
        RefactorDependencyImpact? affectedDependencies)
    {
        var candidates = new List<RefactorCandidate>();

        foreach (var action in actions)
        {
            if (!TryCreateCandidate(action, affectedDependencies, out var candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        return candidates.ToArray();
    }

    private static bool TryCreateCandidate(
        JsonElement action,
        RefactorDependencyImpact? affectedDependencies,
        out RefactorCandidate candidate)
    {
        candidate = null!;

        if (action.ValueKind != JsonValueKind.Object
            || !action.TryGetProperty("title", out var title)
            || title.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(title.GetString())
            || !action.TryGetProperty("edit", out var edit)
            || !TryGetWorkspaceEdit(edit, out var preview)
            || !action.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !HasSafeWriteFlag(data)
            || IsStale(data)
            || !TryGetConfidence(data, out var confidence))
        {
            return false;
        }

        string? rule = GetOptionalString(data, "rule");

        if (rule is null)
        {
            return false;
        }

        string kind = GetStringOrDefault(action, "kind", "quickfix");
        string? applicability = GetOptionalString(data, "applicability");
        IReadOnlyList<DiagnosticReference> references = GetReferences(data);
        RefactorDependencyImpact impact = affectedDependencies is null
            ? EmptyDependencyImpact()
            : affectedDependencies;

        candidate = new RefactorCandidate(
            title.GetString()!,
            kind,
            rule,
            confidence,
            applicability,
            references,
            impact,
            preview,
            WritesFile: false);

        return true;
    }

    private static bool HasSafeWriteFlag(JsonElement data)
    {
        return data.TryGetProperty("writesFile", out var writesFile)
            && writesFile.ValueKind == JsonValueKind.False;
    }

    private static bool IsStale(JsonElement data)
    {
        return data.TryGetProperty("stale", out var stale)
            && stale.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetConfidence(JsonElement data, out string confidence)
    {
        confidence = "";

        if (data.TryGetProperty("diagnosticConfidence", out var diagnosticConfidence)
            && diagnosticConfidence.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(diagnosticConfidence.GetString()))
        {
            confidence = diagnosticConfidence.GetString()!;

            return true;
        }

        if (data.TryGetProperty("confidence", out var metadataConfidence)
            && metadataConfidence.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(metadataConfidence.GetString()))
        {
            confidence = metadataConfidence.GetString()!;

            return true;
        }

        return false;
    }

    private static bool TryGetWorkspaceEdit(JsonElement edit, out RefactorWorkspaceEdit workspaceEdit)
    {
        workspaceEdit = null!;

        if (edit.ValueKind != JsonValueKind.Object
            || !edit.TryGetProperty("changes", out var changes)
            || changes.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var fileChanges = new Dictionary<string, IReadOnlyList<RefactorTextEdit>>(StringComparer.Ordinal);

        foreach (var file in changes.EnumerateObject())
        {
            if (file.Value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var edits = new List<RefactorTextEdit>();

            foreach (var change in file.Value.EnumerateArray())
            {
                if (!TryGetTextEdit(change, out var textEdit))
                {
                    return false;
                }

                edits.Add(textEdit);
            }

            if (edits.Count == 0)
            {
                return false;
            }

            fileChanges[file.Name] = edits;
        }

        if (fileChanges.Count == 0)
        {
            return false;
        }

        workspaceEdit = new RefactorWorkspaceEdit(fileChanges);

        return true;
    }

    private static bool TryGetTextEdit(JsonElement value, out RefactorTextEdit textEdit)
    {
        textEdit = null!;

        if (!value.TryGetProperty("range", out var range)
            || !TryGetRange(range, out var parsedRange)
            || !value.TryGetProperty("newText", out var newText)
            || newText.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        textEdit = new RefactorTextEdit(parsedRange, newText.GetString()!);

        return true;
    }

    private static bool TryGetRange(JsonElement value, out RefactorRange range)
    {
        range = null!;

        if (!value.TryGetProperty("start", out var start)
            || !TryGetPosition(start, out var parsedStart)
            || !value.TryGetProperty("end", out var end)
            || !TryGetPosition(end, out var parsedEnd))
        {
            return false;
        }

        range = new RefactorRange(parsedStart, parsedEnd);

        return true;
    }

    private static bool TryGetPosition(JsonElement value, out RefactorPosition position)
    {
        position = null!;

        if (!value.TryGetProperty("line", out var line)
            || !line.TryGetInt32(out var lineValue)
            || !value.TryGetProperty("character", out var character)
            || !character.TryGetInt32(out var characterValue)
            || lineValue < 0
            || characterValue < 0)
        {
            return false;
        }

        position = new RefactorPosition(lineValue, characterValue);

        return true;
    }

    private static IReadOnlyList<DiagnosticReference> GetReferences(JsonElement data)
    {
        if (!data.TryGetProperty("references", out var references)
            || references.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DiagnosticReference>();

        foreach (var reference in references.EnumerateArray())
        {
            if (!TryGetReference(reference, out var parsedReference))
            {
                continue;
            }

            result.Add(parsedReference);
        }

        return result;
    }

    private static bool TryGetReference(JsonElement value, out DiagnosticReference reference)
    {
        reference = null!;

        string? title = GetOptionalString(value, "title");
        string? note = GetOptionalString(value, "note");
        string? url = GetOptionalString(value, "url");

        if (title is null || note is null || url is null)
        {
            return false;
        }

        reference = new DiagnosticReference(title, note, url);

        return true;
    }

    private static string GetStringOrDefault(JsonElement value, string propertyName, string defaultValue)
    {
        string? text = GetOptionalString(value, propertyName);

        if (text is null)
        {
            return defaultValue;
        }

        return text;
    }

    private static string? GetOptionalString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = property.GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text;
    }

    private static RefactorDependencyImpact EmptyDependencyImpact()
    {
        return new RefactorDependencyImpact("complete", null, []);
    }
}
