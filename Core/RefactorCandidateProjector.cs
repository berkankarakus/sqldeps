using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Core;

/// <summary>Projects validated LSP action payloads into safe refactor candidates.</summary>
public static class RefactorCandidateProjector
{
    /// <summary>Projects safe candidates without dependency-impact enrichment.</summary>
    /// <param name="actions">LSP code actions returned for the current document.</param>
    public static RefactorCandidate[] Project(IEnumerable<JsonElement> actions)
    {
        return Project(actions, null);
    }

    /// <summary>Projects safe candidates and attaches static dependency impact when available.</summary>
    /// <param name="actions">LSP code actions returned for the current document.</param>
    /// <param name="affectedDependencies">Precomputed impact for the candidate's source object.</param>
    public static RefactorCandidate[] Project(
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

        if (!TryGetActionParts(action, out string title, out RefactorWorkspaceEdit preview, out JsonElement data))
        {
            return false;
        }

        if (!TryGetCandidateMetadata(
                data,
                out string rule,
                out string confidence,
                out string? applicability,
                out IReadOnlyList<DiagnosticReference> references))
        {
            return false;
        }

        RefactorDependencyImpact impact = affectedDependencies ?? EmptyDependencyImpact();

        candidate = new RefactorCandidate(
            title,
            GetStringOrDefault(action, "kind", "quickfix"),
            rule,
            confidence,
            applicability,
            references,
            impact,
            preview,
            WritesFile: false);

        return true;
    }

    private static bool TryGetActionParts(
        JsonElement action,
        out string title,
        out RefactorWorkspaceEdit preview,
        out JsonElement data)
    {
        title = "";
        preview = null!;
        data = default;

        string? actionTitle = GetOptionalString(action, "title");
        if (actionTitle is null)
        {
            return false;
        }

        if (!action.TryGetProperty("edit", out JsonElement edit)
            || !TryGetWorkspaceEdit(edit, out preview))
        {
            return false;
        }

        if (!action.TryGetProperty("data", out data))
        {
            return false;
        }

        title = actionTitle;

        return true;
    }

    private static bool TryGetCandidateMetadata(
        JsonElement data,
        out string rule,
        out string confidence,
        out string? applicability,
        out IReadOnlyList<DiagnosticReference> references)
    {
        rule = "";
        confidence = "";
        applicability = null;
        references = [];

        if (!IsEligibleCandidateData(data)
            || !TryGetConfidence(data, out confidence))
        {
            return false;
        }

        string? candidateRule = GetOptionalString(data, "rule");
        if (candidateRule is null || !TryGetReferences(data, out references))
        {
            return false;
        }

        rule = candidateRule;
        applicability = GetOptionalString(data, "applicability");

        return true;
    }

    private static bool IsEligibleCandidateData(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (data.TryGetProperty("isRefactorCandidate", out JsonElement candidateFlag)
            && candidateFlag.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return HasSafeWriteFlag(data) && !IsStale(data);
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

        if (TryGetConfidenceValue(data, "diagnosticConfidence", out confidence))
        {
            return IsSupportedConfidence(confidence);
        }

        if (TryGetConfidenceValue(data, "confidence", out confidence))
        {
            return IsSupportedConfidence(confidence);
        }

        return false;
    }

    private static bool TryGetConfidenceValue(
        JsonElement data,
        string propertyName,
        out string confidence
    )
    {
        confidence = "";

        if (!data.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            return false;
        }

        confidence = value.GetString()!;

        return true;
    }

    private static bool IsSupportedConfidence(string confidence)
    {
        return confidence.Equals("high", StringComparison.OrdinalIgnoreCase)
            || confidence.Equals("medium", StringComparison.OrdinalIgnoreCase)
            || confidence.Equals("low", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWorkspaceEdit(JsonElement edit, out RefactorWorkspaceEdit workspaceEdit)
    {
        workspaceEdit = null!;

        if (!TryGetChanges(edit, out JsonElement changes))
        {
            return false;
        }

        var fileChanges = new Dictionary<string, IReadOnlyList<RefactorTextEdit>>(StringComparer.Ordinal);

        foreach (var file in changes.EnumerateObject())
        {
            if (!TryGetFileEdits(file.Value, out IReadOnlyList<RefactorTextEdit> edits))
            {
                return false;
            }

            fileChanges[file.Name] = edits;
        }

        if (fileChanges.Count == 0)
        {
            return false;
        }

        if (fileChanges.Count != 1)
        {
            return false;
        }

        workspaceEdit = new RefactorWorkspaceEdit(fileChanges);

        return true;
    }

    private static bool TryGetChanges(JsonElement edit, out JsonElement changes)
    {
        changes = default;

        return edit.ValueKind == JsonValueKind.Object
            && edit.TryGetProperty("changes", out changes)
            && changes.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetFileEdits(
        JsonElement fileChanges,
        out IReadOnlyList<RefactorTextEdit> edits)
    {
        edits = [];

        if (fileChanges.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<RefactorTextEdit> parsedEdits = [];

        foreach (JsonElement change in fileChanges.EnumerateArray())
        {
            if (!TryGetTextEdit(change, out RefactorTextEdit textEdit))
            {
                return false;
            }

            parsedEdits.Add(textEdit);
        }

        if (parsedEdits.Count == 0)
        {
            return false;
        }

        edits = parsedEdits;

        return true;
    }

    private static bool TryGetTextEdit(JsonElement value, out RefactorTextEdit textEdit)
    {
        textEdit = null!;

        if (!value.TryGetProperty("range", out var range))
        {
            return false;
        }

        if (!TryGetRange(range, out var parsedRange))
        {
            return false;
        }

        if (!value.TryGetProperty("newText", out var newText))
        {
            return false;
        }

        if (newText.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        textEdit = new RefactorTextEdit(parsedRange, newText.GetString()!);

        return true;
    }

    private static bool TryGetRange(JsonElement value, out RefactorRange range)
    {
        range = null!;

        if (!value.TryGetProperty("start", out var start))
        {
            return false;
        }

        if (!TryGetPosition(start, out var parsedStart))
        {
            return false;
        }

        if (!value.TryGetProperty("end", out var end))
        {
            return false;
        }

        if (!TryGetPosition(end, out var parsedEnd))
        {
            return false;
        }

        range = new RefactorRange(parsedStart, parsedEnd);

        return true;
    }

    private static bool TryGetPosition(JsonElement value, out RefactorPosition position)
    {
        position = null!;

        if (!value.TryGetProperty("line", out var line))
        {
            return false;
        }

        if (!line.TryGetInt32(out var lineValue))
        {
            return false;
        }

        if (!value.TryGetProperty("character", out var character))
        {
            return false;
        }

        if (!character.TryGetInt32(out var characterValue))
        {
            return false;
        }

        if (lineValue < 0)
        {
            return false;
        }

        if (characterValue < 0)
        {
            return false;
        }

        position = new RefactorPosition(lineValue, characterValue);

        return true;
    }

    private static bool TryGetReferences(
        JsonElement data,
        out IReadOnlyList<DiagnosticReference> references)
    {
        references = [];

        if (!data.TryGetProperty("references", out var referencesValue))
        {
            return true;
        }

        if (referencesValue.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<DiagnosticReference>();

        foreach (var reference in referencesValue.EnumerateArray())
        {
            if (!TryGetReference(reference, out var parsedReference))
            {
                return false;
            }

            result.Add(parsedReference);
        }

        references = result;

        return true;
    }

    private static bool TryGetReference(JsonElement value, out DiagnosticReference reference)
    {
        reference = null!;

        string? title = GetOptionalString(value, "title");

        if (title is null)
        {
            return false;
        }

        string? note = GetOptionalString(value, "note");

        if (note is null)
        {
            return false;
        }

        string? url = GetOptionalString(value, "url");

        if (url is null)
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
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
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
