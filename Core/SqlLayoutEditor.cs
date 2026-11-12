using System.Text;

namespace SqlDeps.Core;

/// <summary>
/// Line-level layout edits keyed by the document's original lines and tokens: moving a block of lines to a new
/// indentation and starting a new line before a token. Nothing inside a multi-line string or comment changes.
/// </summary>
internal sealed class SqlLayoutEditor
{
    private readonly SqlLayoutDocument document;
    private readonly int[] indentDelta;
    private readonly SortedDictionary<int, int>[] breaks;

    internal SqlLayoutEditor(SqlLayoutDocument document)
    {
        this.document = document;
        indentDelta = new int[document.LineCount];
        breaks = new SortedDictionary<int, int>[document.LineCount];
    }

    /// <summary>The indentation the line currently has after the edits registered so far.</summary>
    internal int FinalIndent(int line)
    {
        return Math.Max(document.OriginalIndent(line) + indentDelta[line], 0);
    }

    /// <summary>Moves the lines so that text which started at the original column starts at the target column.</summary>
    internal void MoveBlock(int firstLine, int lastLine, int originalColumn, int targetColumn)
    {
        for (var line = Math.Max(firstLine, 0); line <= Math.Min(lastLine, document.LineCount - 1); line++)
        {
            if (!document.StartsInsideToken(line))
            {
                indentDelta[line] = targetColumn - originalColumn;
            }
        }
    }

    /// <summary>Sets the indentation of one whole line (for example a comment between list elements).</summary>
    internal void SetIndent(int line, int absoluteIndent)
    {
        if (!document.StartsInsideToken(line))
        {
            indentDelta[line] = absoluteIndent - document.OriginalIndent(line);
        }
    }

    /// <summary>Starts a new line with the given absolute indentation before the token.</summary>
    internal void BreakBefore(int tokenIndex, int absoluteIndent)
    {
        var offset = document.Tokens[tokenIndex].Offset;
        var line = document.LineOf(offset);

        breaks[line] ??= [];
        breaks[line][offset] = Math.Max(absoluteIndent, 0);
    }

    internal string Build()
    {
        var text = document.Text;
        List<string> output = new(document.LineCount);

        for (var line = 0; line < document.LineCount; line++)
        {
            var start = document.LineStart(line);
            var end = line + 1 < document.LineCount ? document.LineStart(line + 1) - 1 : text.Length;
            var cuts = breaks[line];

            if (cuts is null)
            {
                output.Add(RenderLine(line, text[start..end], indentDelta[line] != 0, trimEnd: false));

                continue;
            }

            var boundaries = new List<int> { start };
            boundaries.AddRange(cuts.Keys);
            boundaries.Add(end);

            for (var index = 0; index < boundaries.Count - 1; index++)
            {
                var segment = text[boundaries[index]..boundaries[index + 1]];

                if (index == 0)
                {
                    var rendered = RenderLine(line, segment, indentDelta[line] != 0, trimEnd: true);

                    if (rendered.Trim().Length > 0 || boundaries.Count == 2)
                    {
                        output.Add(rendered);
                    }

                    continue;
                }

                var trimmed = segment.TrimEnd(' ', '\t');

                if (trimmed.Length > 0)
                {
                    output.Add(new string(' ', cuts[boundaries[index]]) + trimmed);
                }
            }
        }

        return string.Join('\n', output);
    }

    private string RenderLine(int line, string text, bool reindent, bool trimEnd)
    {
        var rendered = text;

        if (reindent && !document.StartsInsideToken(line) && text.Trim().Length > 0)
        {
            rendered = new string(' ', FinalIndent(line)) + text.TrimStart(' ');
        }

        return trimEnd ? rendered.TrimEnd(' ', '\t') : rendered;
    }
}
