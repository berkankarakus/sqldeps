using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Core;

internal static class RefactorCandidateActionReader
{
    internal static bool TryRead(
        JsonElement action,
        out RefactorCandidateAction actionData
    )
    {
        actionData = null!;

        string? title = RefactorCandidateJsonReader.GetOptionalString(action, "title");
        if (title is null)
        {
            return false;
        }

        if (!action.TryGetProperty("edit", out JsonElement edit)
            || !RefactorWorkspaceEditReader.TryRead(edit, out var preview))
        {
            return false;
        }

        if (!action.TryGetProperty("data", out JsonElement data)
            || !RefactorCandidateMetadataReader.TryRead(data, out var metadata))
        {
            return false;
        }

        actionData = new RefactorCandidateAction(
            title,
            RefactorCandidateJsonReader.GetStringOrDefault(action, "kind", "quickfix"),
            metadata.Rule,
            metadata.Confidence,
            metadata.Applicability,
            metadata.References,
            preview);

        return true;
    }
}

internal sealed record RefactorCandidateAction(
    string Title,
    string Kind,
    string Rule,
    string Confidence,
    string? Applicability,
    IReadOnlyList<DiagnosticReference> References,
    RefactorWorkspaceEdit Preview
);

internal static class RefactorCandidateJsonReader
{
    internal static string GetStringOrDefault(
        JsonElement value,
        string propertyName,
        string defaultValue
    )
    {
        string? text = GetOptionalString(value, propertyName);

        if (text is null)
        {
            return defaultValue;
        }

        return text;
    }

    internal static string? GetOptionalString(JsonElement value, string propertyName)
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
}
