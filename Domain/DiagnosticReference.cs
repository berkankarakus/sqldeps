using System.Text.Json.Serialization;

namespace SqlDeps.Domain;

/// <summary>Evidence attached to a diagnostic explanation or experimental suggestion.</summary>
public sealed record DiagnosticReference(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("url")] string Url
);
