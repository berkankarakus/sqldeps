namespace SqlDeps.Lsp;

/// <summary>Validates diagnostic ranges before code actions inspect or edit a document.</summary>
public sealed partial class LanguageServer
{
    private static bool IsDocumentRange(string text, LspRange range)
    {
        if (HasInvalidRangeOrder(range))
        {
            return false;
        }

        var lines = text.Split('\n');

        if (IsOutsideDocument(lines, range))
        {
            return false;
        }

        var startLineLength = lines[range.Start.Line].TrimEnd('\r').Length;
        var endLineLength = lines[range.End.Line].TrimEnd('\r').Length;

        return range.Start.Character <= startLineLength && range.End.Character <= endLineLength;
    }

    private static bool HasInvalidRangeOrder(LspRange range)
    {
        if (range.Start.Line < 0)
        {
            return true;
        }

        if (range.Start.Character < 0)
        {
            return true;
        }

        if (range.End.Line < range.Start.Line)
        {
            return true;
        }

        return range.End.Line == range.Start.Line && range.End.Character < range.Start.Character;
    }

    private static bool IsOutsideDocument(string[] lines, LspRange range)
    {
        return range.Start.Line >= lines.Length || range.End.Line >= lines.Length;
    }
}
