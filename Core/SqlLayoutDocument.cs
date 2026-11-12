using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// A parsed batch with line and token lookups. Layout phases read the real syntax tree and token positions from
/// it instead of guessing from whitespace, so strings and comments are never mistaken for code.
/// </summary>
internal sealed class SqlLayoutDocument
{
    private readonly int[] lineStarts;
    private readonly bool[] startsInsideToken;

    private SqlLayoutDocument(string text, TSqlScript script, IList<TSqlParserToken> tokens)
    {
        Text = text;
        Script = script;
        Tokens = tokens;
        lineStarts = ComputeLineStarts(text);
        startsInsideToken = ComputeLinesInsideTokens(text, tokens, lineStarts);
    }

    internal string Text { get; }

    public TSqlScript Script { get; }

    internal IList<TSqlParserToken> Tokens { get; }

    internal int LineCount => lineStarts.Length;

    /// <summary>Parses the text; returns null when it does not parse, which makes a phase a no-op.</summary>
    internal static SqlLayoutDocument? Parse(string text)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(text);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0 || fragment is not TSqlScript script || script.ScriptTokenStream is null)
        {
            return null;
        }

        return new SqlLayoutDocument(text, script, script.ScriptTokenStream);
    }

    internal int LineOf(int offset)
    {
        var index = Array.BinarySearch(lineStarts, offset);

        if (index >= 0)
        {
            return index;
        }

        return ~index - 1;
    }

    internal int LineStart(int line)
    {
        return lineStarts[line];
    }

    internal int TokenLine(int tokenIndex)
    {
        return LineOf(Tokens[tokenIndex].Offset);
    }

    internal int TokenColumn(int tokenIndex)
    {
        var token = Tokens[tokenIndex];

        return token.Offset - lineStarts[LineOf(token.Offset)];
    }

    /// <summary>The line holding the last character of a fragment.</summary>
    internal int EndLine(TSqlFragment fragment)
    {
        var last = Tokens[fragment.LastTokenIndex];

        return LineOf(last.Offset + Math.Max(TextOf(last).Length - 1, 0));
    }

    /// <summary>The line holding the last character of a token.</summary>
    internal int TokenEndLine(int tokenIndex)
    {
        var token = Tokens[tokenIndex];

        return LineOf(token.Offset + Math.Max(TextOf(token).Length - 1, 0));
    }

    internal int OriginalIndent(int line)
    {
        var start = lineStarts[line];
        var count = 0;

        while (start + count < Text.Length && Text[start + count] == ' ')
        {
            count++;
        }

        return count;
    }

    /// <summary>True for lines that begin inside a multi-line string or comment; their text must not change.</summary>
    internal bool StartsInsideToken(int line)
    {
        return startsInsideToken[line];
    }

    internal bool IsFirstOnLine(int tokenIndex)
    {
        var token = Tokens[tokenIndex];
        var start = lineStarts[LineOf(token.Offset)];

        for (var offset = start; offset < token.Offset; offset++)
        {
            if (Text[offset] != ' ' && Text[offset] != '\t')
            {
                return false;
            }
        }

        return true;
    }

    internal TSqlParserToken FirstTokenOnLine(int line)
    {
        var start = lineStarts[line];
        var low = 0;
        var high = Tokens.Count - 1;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (Tokens[middle].Offset < start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var index = low; index < Tokens.Count; index++)
        {
            if (Tokens[index].TokenType != TSqlTokenType.WhiteSpace)
            {
                return Tokens[index];
            }
        }

        return Tokens[^1];
    }

    /// <summary>True when the first thing on the line is a single-line comment.</summary>
    internal bool IsCommentOnlyLine(int line)
    {
        return !StartsInsideToken(line) && FirstTokenOnLine(line).TokenType == TSqlTokenType.SingleLineComment;
    }

    /// <summary>The nearest later token that is neither white space nor a comment, or -1.</summary>
    internal int NextCode(int tokenIndex)
    {
        for (var index = tokenIndex + 1; index < Tokens.Count; index++)
        {
            var type = Tokens[index].TokenType;

            if (type != TSqlTokenType.WhiteSpace && type != TSqlTokenType.SingleLineComment && type != TSqlTokenType.MultilineComment)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The nearest earlier token that is not white space, or -1.</summary>
    internal int PreviousSignificant(int tokenIndex)
    {
        for (var index = tokenIndex - 1; index >= 0; index--)
        {
            if (Tokens[index].TokenType != TSqlTokenType.WhiteSpace)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The nearest earlier token that is neither whitespace nor a comment, or -1.</summary>
    internal int PreviousCode(int tokenIndex)
    {
        for (var index = tokenIndex - 1; index >= 0; index--)
        {
            var type = Tokens[index].TokenType;

            if (type != TSqlTokenType.WhiteSpace
                && type != TSqlTokenType.SingleLineComment
                && type != TSqlTokenType.MultilineComment)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The nearest later token that is not white space, or -1.</summary>
    internal int NextSignificant(int tokenIndex)
    {
        for (var index = tokenIndex + 1; index < Tokens.Count; index++)
        {
            if (Tokens[index].TokenType != TSqlTokenType.WhiteSpace)
            {
                return index;
            }
        }

        return -1;
    }

    internal int EndOffset(int tokenIndex)
    {
        var token = Tokens[tokenIndex];

        return token.Offset + TextOf(token).Length;
    }

    /// <summary>The text between two tokens when every token in between is white space (or one of the allowed types).</summary>
    internal bool TryGetGap(int fromTokenIndex, int toTokenIndex, out int start, out int length, params TSqlTokenType[] allowed)
    {
        start = EndOffset(fromTokenIndex);
        length = Tokens[toTokenIndex].Offset - start;

        for (var index = fromTokenIndex + 1; index < toTokenIndex; index++)
        {
            var type = Tokens[index].TokenType;

            if (type != TSqlTokenType.WhiteSpace && !allowed.Contains(type))
            {
                return false;
            }
        }

        return length >= 0;
    }

    internal bool IsSingleLine(TSqlFragment fragment)
    {
        return LineOf(fragment.StartOffset) == EndLine(fragment);
    }

    private static int[] ComputeLineStarts(string text)
    {
        List<int> starts = [0];

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private static bool[] ComputeLinesInsideTokens(string text, IList<TSqlParserToken> tokens, int[] lineStarts)
    {
        var inside = new bool[lineStarts.Length];

        foreach (var token in tokens)
        {
            var tokenText = TextOf(token);

            if (token.TokenType == TSqlTokenType.WhiteSpace || !tokenText.Contains('\n'))
            {
                continue;
            }

            var firstLine = LineIndex(lineStarts, token.Offset);
            var lastLine = LineIndex(lineStarts, token.Offset + tokenText.Length - 1);

            for (var line = firstLine + 1; line <= lastLine; line++)
            {
                inside[line] = true;
            }
        }

        return inside;
    }

    /// <summary>The end-of-file token has no text.</summary>
    internal static string TextOf(TSqlParserToken token)
    {
        return token.Text ?? "";
    }

    private static int LineIndex(int[] lineStarts, int offset)
    {
        var index = Array.BinarySearch(lineStarts, offset);

        return index >= 0 ? index : ~index - 1;
    }
}
