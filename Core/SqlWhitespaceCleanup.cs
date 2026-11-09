using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Removes trailing spaces before line breaks; only white space tokens change, never strings or comments.</summary>
internal static partial class SqlWhitespaceCleanup
{
    public static string? Apply(SqlLayoutDocument document)
    {
        SqlTextEdits edits = new();
        var index = 0;

        while (index < document.Tokens.Count)
        {
            if (document.Tokens[index].TokenType != TSqlTokenType.WhiteSpace)
            {
                index++;

                continue;
            }

            // The tokenizer may split " \n" into separate white space tokens, so look at the whole run.
            var runStart = document.Tokens[index].Offset;
            var runEnd = runStart;

            while (index < document.Tokens.Count && document.Tokens[index].TokenType == TSqlTokenType.WhiteSpace)
            {
                runEnd = document.EndOffset(index);
                index++;
            }

            var run = document.Text[runStart..runEnd];
            var cleaned = TrailingSpaces().Replace(run, "\n");

            if (cleaned != run)
            {
                edits.Replace(runStart, run.Length, cleaned);
            }
        }

        return edits.Apply(document.Text);
    }

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();
}
