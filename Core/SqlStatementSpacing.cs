using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// Restores the readability the script generator drops between statements. It pairs the statements of the original
/// and the formatted text through the syntax tree and then
/// <list type="bullet">
/// <item>puts a semicolon back that the generator moved into a following comment,</item>
/// <item>keeps one blank line wherever the source had at least one, and</item>
/// <item>separates multi-line statements from their neighbours with a blank line.</item>
/// </list>
/// </summary>
internal static partial class SqlStatementSpacing
{
    /// <summary>"INT -- note;" becomes "INT;" followed by the comment on its own line.</summary>
    internal static string? RepairSemicolons(SqlLayoutDocument formatted, SqlLayoutDocument original)
    {
        var lists = Pair(formatted, original);

        if (lists is null)
        {
            return null;
        }

        SqlTextEdits edits = new();

        foreach (var (formattedStatement, originalStatement) in lists.SelectMany(list => list.Statements))
        {
            if (!EndsWithSemicolon(original, originalStatement) || EndsWithSemicolon(formatted, formattedStatement))
            {
                continue;
            }

            var end = formattedStatement.LastTokenIndex;
            var comment = formatted.NextSignificant(end);

            if (comment < 0 || formatted.Tokens[comment].TokenType != TSqlTokenType.SingleLineComment)
            {
                continue;
            }

            var text = SqlLayoutDocument.TextOf(formatted.Tokens[comment]);

            if (!text.EndsWith(';'))
            {
                continue;
            }

            var start = formatted.EndOffset(end);
            var indent = new string(' ', formatted.TokenColumn(formattedStatement.FirstTokenIndex));

            edits.Replace(start, formatted.EndOffset(comment) - start, $";\n{indent}{text[..^1].TrimEnd()}");
        }

        return edits.Apply(formatted.Text);
    }

    /// <summary>Keeps source blank lines (at most one) and adds one around multi-line statements.</summary>
    internal static string? Space(SqlLayoutDocument formatted, SqlLayoutDocument original)
    {
        var lists = Pair(formatted, original);

        if (lists is null)
        {
            return null;
        }

        SqlTextEdits edits = new();

        foreach (var list in lists)
        {
            for (var index = 0; index < list.Statements.Count; index++)
            {
                SpaceBefore(formatted, original, list, index, edits);
            }
        }

        return edits.Apply(formatted.Text);
    }

    private static void SpaceBefore(
        SqlLayoutDocument formatted,
        SqlLayoutDocument original,
        PairedList list,
        int index,
        SqlTextEdits edits)
    {
        int formattedPrevious;
        int originalPrevious;

        if (index == 0)
        {
            if (list.FormattedAnchor is null || list.OriginalAnchor is null)
            {
                return;
            }

            formattedPrevious = list.FormattedAnchor.Value;
            originalPrevious = list.OriginalAnchor.Value;
        }
        else
        {
            formattedPrevious = StatementEnd(formatted, list.Statements[index - 1].Formatted);
            originalPrevious = StatementEnd(original, list.Statements[index - 1].Original);
        }

        if (!TryFindUnit(formatted, formattedPrevious, out var formattedGapStart, out var formattedUnit)
            || !TryFindUnit(original, originalPrevious, out var originalGapStart, out var originalUnit))
        {
            return;
        }

        var hadBlankLine = BlankLine().IsMatch(
            original.Text[original.EndOffset(originalGapStart)..original.Tokens[originalUnit].Offset]);
        var spansLines = index > 0
            && (!formatted.IsSingleLine(list.Statements[index - 1].Formatted)
                || !formatted.IsSingleLine(list.Statements[index].Formatted));

        if (!formatted.TryGetGap(formattedGapStart, formattedUnit, out var start, out var length))
        {
            return;
        }

        var current = formatted.Text.Substring(start, length);

        if (!current.Contains('\n'))
        {
            return;
        }

        var isFirstProcedureStatement = index == 0 && list.FormattedAnchor == formattedPrevious;
        var lineBreaks = hadBlankLine || spansLines || isFirstProcedureStatement ? "\n\n" : "\n";
        var wanted = lineBreaks + new string(' ', formatted.TokenColumn(formattedUnit));

        if (current != wanted)
        {
            edits.Replace(start, length, wanted);
        }

        SpaceLeadingComments(formatted, original, formattedUnit, originalUnit, edits);
    }

    /// <summary>
    /// Between the comments that lead a statement and the statement itself, a blank line stays only where the
    /// source had one; the generator may add its own after the last comment.
    /// </summary>
    private static void SpaceLeadingComments(
        SqlLayoutDocument formatted,
        SqlLayoutDocument original,
        int formattedUnit,
        int originalUnit,
        SqlTextEdits edits)
    {
        var formattedChain = LeadingChain(formatted, formattedUnit);
        var originalChain = LeadingChain(original, originalUnit);

        if (formattedChain.Count != originalChain.Count)
        {
            return;
        }

        for (var index = 0; index < formattedChain.Count - 1; index++)
        {
            var hadBlankLine = BlankLine().IsMatch(
                original.Text[original.EndOffset(originalChain[index])..original.Tokens[originalChain[index + 1]].Offset]);

            if (!formatted.TryGetGap(formattedChain[index], formattedChain[index + 1], out var start, out var length))
            {
                continue;
            }

            var current = formatted.Text.Substring(start, length);

            if (!current.Contains('\n'))
            {
                continue;
            }

            var wanted = (hadBlankLine ? "\n\n" : "\n") + new string(' ', formatted.TokenColumn(formattedChain[index + 1]));

            if (current != wanted)
            {
                edits.Replace(start, length, wanted);
            }
        }
    }

    /// <summary>The comments in front of a statement followed by the statement's first token.</summary>
    private static List<int> LeadingChain(SqlLayoutDocument document, int unit)
    {
        List<int> chain = [unit];

        while (document.Tokens[chain[^1]].TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
        {
            var next = document.NextSignificant(chain[^1]);

            if (next < 0)
            {
                break;
            }

            chain.Add(next);
        }

        return chain;
    }

    private static List<PairedList>? Pair(SqlLayoutDocument formatted, SqlLayoutDocument original)
    {
        var formattedLists = CollectLists(formatted);
        var originalLists = CollectLists(original);

        if (formattedLists.Count != originalLists.Count)
        {
            return null;
        }

        List<PairedList> pairs = [];

        for (var index = 0; index < formattedLists.Count; index++)
        {
            if (formattedLists[index].Statements.Count != originalLists[index].Statements.Count)
            {
                return null;
            }

            var statements = formattedLists[index].Statements
                .Zip(originalLists[index].Statements, (formattedStatement, originalStatement) => (formattedStatement, originalStatement))
                .ToList();

            pairs.Add(new PairedList(statements, formattedLists[index].Anchor, originalLists[index].Anchor));
        }

        return pairs;
    }

    private static List<RawList> CollectLists(SqlLayoutDocument document)
    {
        ListCollector collector = new(document);
        document.Script.Accept(collector);

        return collector.Lists;
    }

    private static bool EndsWithSemicolon(SqlLayoutDocument document, TSqlStatement statement)
    {
        return document.Tokens[StatementEnd(document, statement)].TokenType == TSqlTokenType.Semicolon;
    }

    /// <summary>The last token of a statement, including a semicolon that belongs to it.</summary>
    private static int StatementEnd(SqlLayoutDocument document, TSqlStatement statement)
    {
        var last = statement.LastTokenIndex;
        var next = document.NextSignificant(last);

        if (document.Tokens[last].TokenType != TSqlTokenType.Semicolon
            && next >= 0
            && document.Tokens[next].TokenType == TSqlTokenType.Semicolon)
        {
            return next;
        }

        return last;
    }

    /// <summary>
    /// Finds the first token of the next statement unit after a token. A comment on its own line belongs to the
    /// next statement; a comment on the same line as the previous token stays with the previous statement.
    /// </summary>
    private static bool TryFindUnit(SqlLayoutDocument document, int previousToken, out int gapStart, out int unit)
    {
        gapStart = previousToken;
        unit = -1;
        var endLine = document.TokenEndLine(previousToken);
        var next = document.NextSignificant(previousToken);

        while (next >= 0)
        {
            var isComment = document.Tokens[next].TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;

            if (isComment && document.TokenLine(next) == endLine)
            {
                gapStart = next;
                endLine = document.TokenEndLine(next);
                next = document.NextSignificant(next);

                continue;
            }

            unit = next;

            return true;
        }

        return false;
    }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLine();

    private sealed record RawList(List<TSqlStatement> Statements, int? Anchor);

    private sealed record PairedList(
        List<(TSqlStatement Formatted, TSqlStatement Original)> Statements,
        int? FormattedAnchor,
        int? OriginalAnchor);

    private sealed class ListCollector(SqlLayoutDocument document) : TSqlFragmentVisitor
    {
        public List<RawList> Lists { get; } = [];

        public override void Visit(TSqlBatch node)
        {
            Add(node.Statements);
        }

        public override void Visit(StatementList node)
        {
            Add(node.Statements);
        }

        /// <summary>The anchor is the AS token of a procedure body, so a blank line after AS can be kept.</summary>
        private void Add(IList<TSqlStatement> statements)
        {
            int? anchor = null;

            if (statements.Count > 0)
            {
                var before = document.PreviousSignificant(statements[0].FirstTokenIndex);

                if (before >= 0 && document.Tokens[before].TokenType == TSqlTokenType.As)
                {
                    anchor = before;
                }
            }

            Lists.Add(new RawList(statements.ToList(), anchor));
        }
    }
}
