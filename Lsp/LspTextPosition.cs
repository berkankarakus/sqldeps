namespace SqlDeps.Lsp;

/// <summary>Shared UTF-16 text and identifier lookup primitives used by LSP features.</summary>
internal static class LspTextPosition
{
    public static int OffsetAt(string text, int line, int character)
    {
        var lines = text.Split('\n');
        var precedingLength = lines.Take(Math.Clamp(line, 0, lines.Length)).Sum(item => item.Length + 1);
        var currentLine = line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : "";

        return Math.Min(precedingLength + Math.Max(character, 0), precedingLength + currentLine.Length);
    }

    public static LspRange PositionAt(string text, int offset)
    {
        var bounded = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        var lineStart = 0;

        for (var index = 0; index < bounded; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        var character = bounded - lineStart;

        return new(new(line, character), new(line, character));
    }

    public static string? ColumnIdentifierAt(string text, int line, int character)
    {
        return IdentifierAt(text, line, character, IsColumnNameCharacter, clampToLineEnd: true)
            ?.Replace("[", "")
            .Replace("]", "");
    }

    public static string? ObjectIdentifierAt(string text, int line, int character)
    {
        return IdentifierAt(text, line, character, IsObjectNameCharacter, clampToLineEnd: false);
    }

    private static string? IdentifierAt(
        string text,
        int line,
        int character,
        Func<char, bool> isNameCharacter,
        bool clampToLineEnd)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var source = lines[line].TrimEnd('\r');
        if (!clampToLineEnd && (character < 0 || character > source.Length))
        {
            return null;
        }

        var index = clampToLineEnd
            ? Math.Min(Math.Max(character, 0), source.Length)
            : character;
        var start = index;
        var end = index;

        while (start > 0 && isNameCharacter(source[start - 1]))
        {
            start--;
        }

        while (end < source.Length && isNameCharacter(source[end]))
        {
            end++;
        }

        return start == end ? null : source[start..end];
    }

    private static bool IsColumnNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '.' or '[' or ']';
    }

    private static bool IsObjectNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '#' or '@' or '$' or '.' or '[' or ']' or '"';
    }
}
