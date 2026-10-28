using System.Text;
using System.Text.RegularExpressions;

namespace SqlDeps;

/// <summary>
/// GO is a client directive, not T-SQL, so the parser cannot read it. A script is cut into batches
/// at every line that holds only GO (optionally followed by a repeat count).
/// </summary>
public static class BatchSplitter
{
    private static readonly Regex GoLine = new(@"^\s*GO(\s+\d+)?\s*(--.*)?$", RegexOptions.IgnoreCase);

    public static IReadOnlyList<SqlBatch> Split(string script)
    {
        List<SqlBatch> batches = [];
        StringBuilder current = new();
        var currentStartLine = 1;
        var lineNumber = 0;

        foreach (var line in script.Split('\n'))
        {
            lineNumber++;
            var cleanLine = line.TrimEnd('\r');

            if (GoLine.IsMatch(cleanLine))
            {
                AddBatch(batches, current, currentStartLine);
                current.Clear();
                currentStartLine = lineNumber + 1;

                continue;
            }

            current.Append(cleanLine).Append('\n');
        }

        AddBatch(batches, current, currentStartLine);

        return batches;
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
