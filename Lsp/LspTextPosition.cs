namespace SqlDeps.Lsp;

/// <summary>Shared UTF-16 text and identifier lookup primitives used by LSP features.</summary>
internal static class LspTextPosition
{
    internal static int OffsetAt(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line >= lines.Length)
        {
            return text.Length;
        }

        var precedingLength = lines.Take(Math.Max(line, 0)).Sum(item => item.Length + 1);
        var currentLine = line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : "";

        return Math.Min(precedingLength + Math.Max(character, 0), precedingLength + currentLine.Length);
    }

    internal static LspRange PositionAt(string text, int offset)
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

        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
        {
            lineEnd--;
        }

        var character = Math.Min(bounded - lineStart, lineEnd - lineStart);

        return new(new(line, character), new(line, character));
    }

    internal static string? ColumnIdentifierAt(string text, int line, int character)
    {
        return IdentifierAt(text, line, character, IsColumnNameCharacter, clampToLineEnd: true)
            ?.Replace("[", "")
            .Replace("]", "");
    }

    internal static string? ObjectIdentifierAt(string text, int line, int character)
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
