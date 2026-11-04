using System.Text;

namespace SqlDeps;

/// <summary>
/// GO is a client directive, not T-SQL, so the parser cannot read it. A script is cut into batches
/// at every line that holds only GO (optionally followed by a repeat count). A small recovery path
/// also handles extracted SQL Server preambles where GO was placed on the same line as SET options.
/// </summary>
public static class BatchSplitter
{
    public static IReadOnlyList<SqlBatch> Split(string script)
    {
        List<SqlBatch> batches = [];
        StringBuilder current = new();
        var currentStartLine = 1;
        var lineNumber = 0;

        var state = new LexicalState();
        var lineStart = 0;

        while (lineStart <= script.Length)
        {
            var lineEnd = script.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = script.Length;
            }

            var line = script.AsSpan(lineStart, lineEnd - lineStart);
            if (!line.IsEmpty && line[^1] == '\r')
            {
                line = line[..^1];
            }

            lineNumber++;

            if (state.IsOutside && TrySplitInlineBatchDirective(line, out var beforeGo, out var afterGo))
            {
                current.Append(beforeGo).Append('\n');
                AddBatch(batches, current, currentStartLine);
                current.Clear();
                currentStartLine = afterGo.IsEmpty ? lineNumber + 1 : lineNumber;

                if (!afterGo.IsEmpty)
                {
                    current.Append(afterGo).Append('\n');
                    state.ReadLine(afterGo);
                }
            }
            else if (state.IsOutside && IsGoLine(line))
            {
                AddBatch(batches, current, currentStartLine);
                current.Clear();
                currentStartLine = lineNumber + 1;
            }
            else
            {
                current.Append(line).Append('\n');
                state.ReadLine(line);
            }

            if (lineEnd == script.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        AddBatch(batches, current, currentStartLine);

        return batches;
    }

    private static bool TrySplitInlineBatchDirective(
        ReadOnlySpan<char> line,
        out ReadOnlySpan<char> beforeGo,
        out ReadOnlySpan<char> afterGo)
    {
        beforeGo = default;
        afterGo = default;

        var start = 0;
        SkipWhitespace(line, ref start);

        foreach (var prefix in new[] { "SET ANSI_NULLS ON", "SET QUOTED_IDENTIFIER ON" })
        {
            if (!line[start..].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var goStart = start + prefix.Length;
            if (goStart < line.Length && !char.IsWhiteSpace(line[goStart]))
            {
                continue;
            }

            SkipWhitespace(line, ref goStart);
            if (!TryReadGoToken(line, ref goStart))
            {
                continue;
            }

            SkipWhitespace(line, ref goStart);
            if (goStart < line.Length && !StartsBatchStatement(line[goStart..]))
            {
                continue;
            }

            beforeGo = line[..(start + prefix.Length)];
            afterGo = line[goStart..];

            return true;
        }

        return false;
    }

    private static bool TryReadGoToken(ReadOnlySpan<char> line, ref int index)
    {
        if (index + 2 > line.Length
            || !IsCharacter(line[index], 'G')
            || !IsCharacter(line[index + 1], 'O'))
        {
            return false;
        }

        index += 2;

        return index == line.Length || char.IsWhiteSpace(line[index]);
    }

    private static bool StartsBatchStatement(ReadOnlySpan<char> text)
    {
        return text.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGoLine(ReadOnlySpan<char> line)
    {
        var index = 0;
        SkipWhitespace(line, ref index);

        if (index + 2 > line.Length)
        {
            return false;
        }

        if (!IsCharacter(line[index], 'G'))
        {
            return false;
        }

        if (!IsCharacter(line[index + 1], 'O'))
        {
            return false;
        }

        index += 2;
        var afterGo = index;
        if (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] != '-')
        {
            return false;
        }

        SkipWhitespace(line, ref index);

        if (index < line.Length && line[index] == '-')
        {
            return index + 1 < line.Length && line[index + 1] == '-';
        }

        while (index < line.Length && char.IsDigit(line[index]))
        {
            index++;
        }

        if (index > afterGo)
        {
            SkipWhitespace(line, ref index);
        }

        return index == line.Length
            || index + 1 < line.Length && line[index] == '-' && line[index + 1] == '-';
    }

    private static void SkipWhitespace(ReadOnlySpan<char> line, ref int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }
    }

    private static bool IsCharacter(char value, char expected) =>
        char.ToUpperInvariant(value) == expected;

    private sealed class LexicalState
    {
        private int blockCommentDepth;
        private bool inString;
        private bool inQuotedIdentifier;

        public bool IsOutside => blockCommentDepth == 0 && !inString && !inQuotedIdentifier;

        public void ReadLine(ReadOnlySpan<char> line)
        {
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';

                if (blockCommentDepth > 0)
                {
                    ReadBlockComment(current, next, ref index);

                    continue;
                }

                if (inString)
                {
                    ReadString(current, next, ref index);

                    continue;
                }

                if (inQuotedIdentifier)
                {
                    ReadQuotedIdentifier(current, next, ref index);

                    continue;
                }

                if (current == '-' && next == '-')
                {
                    return;
                }

                ReadCodeCharacter(current, next, ref index);
            }
        }

        private void ReadCodeCharacter(char current, char next, ref int index)
        {
            if (current == '/' && next == '*')
            {
                blockCommentDepth++;
                index++;

                return;
            }

            if (current == '\'')
            {
                inString = true;

                return;
            }

            if (current == '"')
            {
                inQuotedIdentifier = true;
            }
        }

        private void ReadBlockComment(char current, char next, ref int index)
        {
            if (current == '/' && next == '*')
            {
                blockCommentDepth++;
                index++;

                return;
            }

            if (current == '*' && next == '/')
            {
                blockCommentDepth--;
                index++;
            }
        }

        private void ReadString(char current, char next, ref int index)
        {
            if (current == '\'' && next == '\'')
            {
                index++;

                return;
            }

            if (current == '\'')
            {
                inString = false;
            }
        }

        private void ReadQuotedIdentifier(char current, char next, ref int index)
        {
            if (current == '"' && next == '"')
            {
                index++;

                return;
            }

            if (current == '"')
            {
                inQuotedIdentifier = false;
            }
        }
    }

    private static void AddBatch(
        List<SqlBatch> batches,
        StringBuilder text,
        int startLine
    )
    {
        var value = text.ToString();
        if (value.AsSpan().Trim().IsEmpty)
        {
            return;
        }

        batches.Add(new SqlBatch(value, startLine));
    }
}
