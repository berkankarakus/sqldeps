using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Core;

internal static class RefactorCandidateMetadataReader
{
    public static bool TryRead(
        JsonElement data,
        out RefactorCandidateMetadata metadata
    )
    {
        metadata = null!;

        if (!IsEligible(data)
            || !TryGetConfidence(data, out var confidence))
        {
            return false;
        }

        string? rule = RefactorCandidateJsonReader.GetOptionalString(data, "rule");
        if (rule is null || !TryGetReferences(data, out var references))
        {
            return false;
        }

        metadata = new RefactorCandidateMetadata(
            rule,
            confidence,
            RefactorCandidateJsonReader.GetOptionalString(data, "applicability"),
            references);

        return true;
    }

    private static bool IsEligible(JsonElement data)
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

    private static bool TryGetReferences(
        JsonElement data,
        out IReadOnlyList<DiagnosticReference> references
    )
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

    private static bool TryGetReference(
        JsonElement value,
        out DiagnosticReference reference
    )
    {
        reference = null!;

        string? title = RefactorCandidateJsonReader.GetOptionalString(value, "title");
        string? note = RefactorCandidateJsonReader.GetOptionalString(value, "note");
        string? url = RefactorCandidateJsonReader.GetOptionalString(value, "url");

        if (title is null || note is null || url is null)
        {
            return false;
        }

        reference = new DiagnosticReference(title, note, url);

        return true;
    }
}

internal sealed record RefactorCandidateMetadata(
    string Rule,
    string Confidence,
    string? Applicability,
    IReadOnlyList<DiagnosticReference> References
);
