using System.Text.Json.Serialization;
using SqlDeps.Domain;

namespace SqlDeps.Core;

public sealed record RefactorCandidate(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("rule")] string Rule,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("applicability")] string? Applicability,
    [property: JsonPropertyName("references")] IReadOnlyList<DiagnosticReference> References,
    [property: JsonPropertyName("affected_dependencies")] RefactorDependencyImpact AffectedDependencies,
    [property: JsonPropertyName("preview")] RefactorWorkspaceEdit Preview,
    [property: JsonPropertyName("writes_file")] bool WritesFile,
    [property: JsonPropertyName("source_object")] string? SourceObject = null
);

public sealed record RefactorDependencyImpact(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("items")] IReadOnlyList<RefactorDependencyItem> Items
);

public sealed record RefactorDependencyItem(
    [property: JsonPropertyName("object_name")] string ObjectName,
    [property: JsonPropertyName("impact")] IReadOnlyList<RefactorImpactRow> Impact
);

public sealed record RefactorImpactRow(
    [property: JsonPropertyName("affected")] string? Affected,
    [property: JsonPropertyName("depth")] int? Depth,
    [property: JsonPropertyName("path")] string? Path
);

public sealed record RefactorWorkspaceEdit(
    [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, IReadOnlyList<RefactorTextEdit>> Changes
);

public sealed record RefactorTextEdit(
    [property: JsonPropertyName("range")] RefactorRange Range,
    [property: JsonPropertyName("newText")] string NewText
);

public sealed record RefactorRange(
    [property: JsonPropertyName("start")] RefactorPosition Start,
    [property: JsonPropertyName("end")] RefactorPosition End
);

public sealed record RefactorPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character
);
