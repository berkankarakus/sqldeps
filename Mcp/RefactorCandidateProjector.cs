using System.Text.Json;

namespace SqlDeps.Mcp;

internal static class RefactorCandidateProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static JsonElement[] Project(IEnumerable<JsonElement> actions)
    {
        return Project(actions, null);
    }

    internal static JsonElement[] Project(
        IEnumerable<JsonElement> actions,
        JsonElement? affectedDependencies)
    {
        var candidates = new List<JsonElement>();

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
        JsonElement? affectedDependencies,
        out JsonElement candidate)
    {
        candidate = default;

        if (action.ValueKind != JsonValueKind.Object
            || !action.TryGetProperty("title", out var title)
            || title.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(title.GetString())
            || !action.TryGetProperty("edit", out var edit)
            || edit.ValueKind != JsonValueKind.Object
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
        JsonElement references = GetArrayOrEmpty(data, "references");
        JsonElement dependencyImpact = GetArrayOrObjectOrEmpty(affectedDependencies);

        candidate = JsonSerializer.SerializeToElement(
            new
            {
                title = title.GetString(),
                kind,
                rule,
                confidence,
                applicability,
                references,
                affected_dependencies = dependencyImpact,
                preview = edit.Clone(),
                writes_file = false,
            },
            JsonOptions
        );

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
        if (!value.TryGetProperty(propertyName, out var property)
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

    private static JsonElement GetArrayOrEmpty(JsonElement value, string propertyName)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Array)
        {
            return property.Clone();
        }

        return JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
    }

    private static JsonElement GetArrayOrObjectOrEmpty(JsonElement? value)
    {
        if (value is JsonElement element
            && element.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
        {
            return element.Clone();
        }

        return JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
    }
}
