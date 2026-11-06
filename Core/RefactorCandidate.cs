using System.Text.Json.Serialization;
using SqlDeps.Domain;

namespace SqlDeps.Core;

/// <summary>A read-only, evidence-backed edit suggestion exposed to CLI, LSP, and MCP clients.</summary>
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

/// <summary>Dependency impact discovered for a refactor candidate.</summary>
public sealed record RefactorDependencyImpact(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("items")] IReadOnlyList<RefactorDependencyItem> Items
);

/// <summary>Impact rows associated with one affected database object.</summary>
public sealed record RefactorDependencyItem(
    [property: JsonPropertyName("object_name")] string ObjectName,
    [property: JsonPropertyName("impact")] IReadOnlyList<RefactorImpactRow> Impact
);

/// <summary>A single downstream dependency path and its traversal depth.</summary>
public sealed record RefactorImpactRow(
    [property: JsonPropertyName("affected")] string? Affected,
    [property: JsonPropertyName("depth")] int? Depth,
    [property: JsonPropertyName("path")] string? Path
);

/// <summary>A non-writing workspace edit preview keyed by document URI.</summary>
public sealed record RefactorWorkspaceEdit(
    [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, IReadOnlyList<RefactorTextEdit>> Changes
);

/// <summary>A text replacement within a refactor preview.</summary>
public sealed record RefactorTextEdit(
    [property: JsonPropertyName("range")] RefactorRange Range,
    [property: JsonPropertyName("newText")] string NewText
);

/// <summary>A zero-based text range used by a refactor preview.</summary>
public sealed record RefactorRange(
    [property: JsonPropertyName("start")] RefactorPosition Start,
    [property: JsonPropertyName("end")] RefactorPosition End
);

/// <summary>A zero-based line and UTF-16 character position.</summary>
public sealed record RefactorPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character
);
