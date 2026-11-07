using System.Text.Json.Serialization;
using SqlDeps.Domain;

namespace SqlDeps.Core;

/// <summary>A read-only, evidence-backed edit suggestion exposed to CLI, LSP, and MCP clients.</summary>
/// <param name="Title">The client-facing action title.</param>
/// <param name="Kind">The LSP action kind.</param>
/// <param name="Rule">The diagnostic rule that produced the action.</param>
/// <param name="Confidence">The diagnostic confidence level.</param>
/// <param name="Applicability">The safety conditions required for the preview.</param>
/// <param name="References">Evidence links supporting the suggestion.</param>
/// <param name="AffectedDependencies">Static downstream impact of the source object.</param>
/// <param name="Preview">The non-writing workspace edit preview.</param>
/// <param name="WritesFile">Whether applying the action writes files directly.</param>
/// <param name="SourceObject">The indexed source object associated with the diagnostic.</param>
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
/// <param name="Status">The impact completeness, such as <c>complete</c> or <c>partial</c>.</param>
/// <param name="Reason">Why the impact is incomplete, when applicable.</param>
/// <param name="Items">Affected objects and their dependency paths.</param>
public sealed record RefactorDependencyImpact(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("items")] IReadOnlyList<RefactorDependencyItem> Items
);

/// <summary>Impact rows associated with one affected database object.</summary>
/// <param name="ObjectName">The affected indexed object.</param>
/// <param name="Impact">Dependency paths that reach the object.</param>
public sealed record RefactorDependencyItem(
    [property: JsonPropertyName("object_name")] string ObjectName,
    [property: JsonPropertyName("impact")] IReadOnlyList<RefactorImpactRow> Impact
);

/// <summary>A single downstream dependency path and its traversal depth.</summary>
/// <param name="Affected">The affected downstream object, when known.</param>
/// <param name="Depth">The graph traversal depth, when known.</param>
/// <param name="Path">The dependency path, when known.</param>
public sealed record RefactorImpactRow(
    [property: JsonPropertyName("affected")] string? Affected,
    [property: JsonPropertyName("depth")] int? Depth,
    [property: JsonPropertyName("path")] string? Path
);

/// <summary>A non-writing workspace edit preview keyed by document URI.</summary>
/// <param name="Changes">Text edits grouped by document URI.</param>
public sealed record RefactorWorkspaceEdit(
    [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, IReadOnlyList<RefactorTextEdit>> Changes
);

/// <summary>A text replacement within a refactor preview.</summary>
/// <param name="Range">The zero-based range to replace.</param>
/// <param name="NewText">The replacement text.</param>
public sealed record RefactorTextEdit(
    [property: JsonPropertyName("range")] RefactorRange Range,
    [property: JsonPropertyName("newText")] string NewText
);

/// <summary>A zero-based text range used by a refactor preview.</summary>
/// <param name="Start">The inclusive start position.</param>
/// <param name="End">The exclusive end position.</param>
public sealed record RefactorRange(
    [property: JsonPropertyName("start")] RefactorPosition Start,
    [property: JsonPropertyName("end")] RefactorPosition End
);

/// <summary>A zero-based line and UTF-16 character position.</summary>
/// <param name="Line">The zero-based line number.</param>
/// <param name="Character">The zero-based UTF-16 character offset.</param>
public sealed record RefactorPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character
);
