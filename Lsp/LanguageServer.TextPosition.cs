namespace SqlDeps.Lsp;

/// <summary>Provides the shared text-position primitives to the language-server partials.</summary>
public sealed partial class LanguageServer
{
    private static int OffsetAt(string text, int line, int character)
    {
        return LspTextPosition.OffsetAt(text, line, character);
    }

    private static LspRange PositionAt(string text, int offset)
    {
        return LspTextPosition.PositionAt(text, offset);
    }

    private static string? IdentifierAt(string text, int line, int character)
    {
        return LspTextPosition.ColumnIdentifierAt(text, line, character);
    }
}
