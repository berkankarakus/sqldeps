using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// Shortens INNER/OUTER join keywords and puts simple joins on one line: "JOIN table ON condition". A join whose
/// table is a multi-line derived table, or whose condition spans lines, keeps those parts on their own lines.
/// </summary>
internal sealed class SqlJoinLayoutVisitor : TSqlFragmentVisitor
{
    private readonly SqlLayoutDocument document;
    private readonly SqlTextEdits edits = new();

    private SqlJoinLayoutVisitor(SqlLayoutDocument document)
    {
        this.document = document;
    }

    internal static string? Apply(SqlLayoutDocument document)
    {
        SqlJoinLayoutVisitor visitor = new(document);
        document.Script.Accept(visitor);

        return visitor.edits.Apply(document.Text);
    }

    public override void Visit(QualifiedJoin node)
    {
        var headerFirst = node.FirstTableReference.LastTokenIndex + 1;
        var headerLast = node.SecondTableReference.FirstTokenIndex - 1;
        List<int> keywords = [];

        for (var index = headerFirst; index <= headerLast; index++)
        {
            if (document.Tokens[index].TokenType != TSqlTokenType.WhiteSpace)
            {
                keywords.Add(index);
            }
        }

        if (keywords.Count == 0 || document.Tokens[keywords[^1]].TokenType != TSqlTokenType.Join)
        {
            return;
        }

        ShortenKeywords(keywords);

        if (!document.IsSingleLine(node.SecondTableReference))
        {
            return;
        }

        CollapseOntoOneLine(node, keywords[^1]);
    }

    /// <summary>INNER JOIN becomes JOIN and LEFT/RIGHT/FULL OUTER JOIN loses OUTER, unless a join hint is present.</summary>
    private void ShortenKeywords(List<int> keywords)
    {
        var types = keywords.Select(index => document.Tokens[index].TokenType).ToArray();
        var isInner = types.SequenceEqual([TSqlTokenType.Inner, TSqlTokenType.Join]);
        var isOuter = types.Length == 3
            && types[1] == TSqlTokenType.Outer
            && types[2] == TSqlTokenType.Join
            && types[0] is TSqlTokenType.Left or TSqlTokenType.Right or TSqlTokenType.Full;

        if (!isInner && !isOuter)
        {
            return;
        }

        var removed = isInner ? keywords[0] : keywords[1];
        var following = keywords[isInner ? 1 : 2];

        edits.Replace(document.Tokens[removed].Offset, document.Tokens[following].Offset - document.Tokens[removed].Offset, "");
    }

    private void CollapseOntoOneLine(QualifiedJoin node, int joinToken)
    {
        var table = node.SecondTableReference;

        if (document.TryGetGap(joinToken, table.FirstTokenIndex, out var tableStart, out var tableLength) && document.Text.Substring(tableStart, tableLength) != " ")
        {
            edits.Replace(tableStart, tableLength, " ");
        }

        if (node.SearchCondition is null || !document.IsSingleLine(node.SearchCondition))
        {
            return;
        }

        var onToken = document.NextSignificant(table.LastTokenIndex);

        if (onToken < 0 || document.Tokens[onToken].TokenType != TSqlTokenType.On)
        {
            return;
        }

        if (document.TryGetGap(table.LastTokenIndex, onToken, out var onStart, out var onLength) && document.Text.Substring(onStart, onLength) != " ")
        {
            edits.Replace(onStart, onLength, " ");
        }
    }
}
