using System.Text;
using System.Text.RegularExpressions;

namespace SqlDeps;

/// <summary>
/// GO is a client directive, not T-SQL, so the parser cannot read it. A script is cut into batches
/// at every line that holds only GO (optionally followed by a repeat count).
/// </summary>
public static partial class BatchSplitter
{
    [GeneratedRegex(@"^\s*GO(\s+\d+)?\s*(--.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    public static IReadOnlyList<SqlBatch> Split(string script)
    {
        List<SqlBatch> batches = [];
        StringBuilder current = new();
        var currentStartLine = 1;
        var lineNumber = 0;

        var state = new LexicalState();

        foreach (var line in script.Split('\n'))
        {
            lineNumber++;
            var cleanLine = line.TrimEnd('\r');

            if (state.IsOutside && GoLine().IsMatch(cleanLine))
            {
                AddBatch(batches, current, currentStartLine);
                current.Clear();
                currentStartLine = lineNumber + 1;

                continue;
            }

            current.Append(cleanLine).Append('\n');
            state.ReadLine(cleanLine);
        }

        AddBatch(batches, current, currentStartLine);

        return batches;
    }

    private sealed class LexicalState
    {
        private int blockCommentDepth;
        private bool inString;
        private bool inQuotedIdentifier;

        public bool IsOutside => blockCommentDepth == 0 && !inString && !inQuotedIdentifier;

        public void ReadLine(string line)
        {
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';

                if (blockCommentDepth > 0)
                {
                    if (current == '/' && next == '*')
                    {
                        blockCommentDepth++;
                        index++;
                    }
                    else if (current == '*' && next == '/')
                    {
                        blockCommentDepth--;
                        index++;
                    }

                    continue;
                }

                if (inString)
                {
                    if (current == '\'' && next == '\'')
                    {
                        index++;
                    }
                    else if (current == '\'')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inQuotedIdentifier)
                {
                    if (current == '"' && next == '"')
                    {
                        index++;
                    }
                    else if (current == '"')
                    {
                        inQuotedIdentifier = false;
                    }

                    continue;
                }

                if (current == '-' && next == '-')
                {
                    return;
                }

                if (current == '/' && next == '*')
                {
                    blockCommentDepth++;
                    index++;
                }
                else if (current == '\'')
                {
                    inString = true;
                }
                else if (current == '"')
                {
                    inQuotedIdentifier = true;
                }
            }
        }
    }

    private static void AddBatch(
        List<SqlBatch> batches,
        StringBuilder text,
        int startLine
    )
    {
        if (text.ToString().Trim().Length == 0)
        {
            return;
        }

        batches.Add(new SqlBatch(text.ToString(), startLine));
    }
}
