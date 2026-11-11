using System.Text.Json.Serialization;

namespace SqlDeps.Lsp;

/// <summary>Zero-based UTF-16 position used by the local LSP protocol types.</summary>
internal sealed record LspPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character
);

/// <summary>Half-open document range used by the local LSP protocol types.</summary>
internal sealed record LspRange(
    [property: JsonPropertyName("start")] LspPosition Start,
    [property: JsonPropertyName("end")] LspPosition End
);
