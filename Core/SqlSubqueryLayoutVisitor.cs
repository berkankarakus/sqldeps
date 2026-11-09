using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// Puts the parentheses of every subquery on their own lines and indents the query inside by four spaces per
/// nesting level. The parentheses come from the syntax tree, so strings, comments and bracketed identifiers can
/// never be mistaken for them.
/// </summary>
internal sealed class SqlSubqueryLayoutVisitor : TSqlFragmentVisitor
{
    private const int IndentSize = 4;

    private readonly SqlLayoutDocument document;
    private readonly SortedDictionary<int, Subquery> subqueries = [];

    private SqlSubqueryLayoutVisitor(SqlLayoutDocument document)
    {
        this.document = document;
    }

    public static string? Apply(SqlLayoutDocument document)
    {
        SqlSubqueryLayoutVisitor visitor = new(document);
        document.Script.Accept(visitor);

        if (visitor.subqueries.Count == 0)
        {
            return null;
        }

        return visitor.Rewrite();
    }

    public override void Visit(ScalarSubquery node)
    {
        Collect(node.QueryExpression);
    }

    public override void Visit(QueryDerivedTable node)
    {
        Collect(node.QueryExpression);
    }

    public override void Visit(CommonTableExpression node)
    {
        Collect(node.QueryExpression);
    }

    private void Collect(QueryExpression? query)
    {
        if (query is null)
        {
            return;
        }

        var open = document.PreviousSignificant(query.FirstTokenIndex);
        var close = document.NextSignificant(query.LastTokenIndex);

        if (open < 0 || close < 0
            || document.Tokens[open].TokenType != TSqlTokenType.LeftParenthesis
            || document.Tokens[close].TokenType != TSqlTokenType.RightParenthesis)
        {
            return;
        }

        subqueries[open] = new Subquery(open, close, query);
    }

    /// <summary>Handles outer subqueries before the ones nested in them, so indentation accumulates by level.</summary>
    private string Rewrite()
    {
        SqlLayoutEditor editor = new(document);

        foreach (var subquery in subqueries.Values)
        {
            Layout(editor, subquery);
        }

        return editor.Build();
    }

    private void Layout(SqlLayoutEditor editor, Subquery subquery)
    {
        var openLine = document.TokenLine(subquery.Open);
        var firstInner = subquery.Query.FirstTokenIndex;
        var closeLine = document.TokenLine(subquery.Close);
        var openFirst = document.IsFirstOnLine(subquery.Open);
        var innerFirst = document.IsFirstOnLine(firstInner);
        var closeFirst = document.IsFirstOnLine(subquery.Close);
        var indent = editor.FinalIndent(openLine) + (openFirst ? 0 : PredicateOffset(openLine));
        var innerIndent = indent + IndentSize;

        if (openFirst && innerFirst && closeFirst
            && document.TokenLine(firstInner) > openLine
            && closeLine > document.EndLine(subquery.Query)
            && editor.FinalIndent(document.TokenLine(firstInner)) == innerIndent
            && editor.FinalIndent(closeLine) == indent)
        {
            return;
        }

        if (!openFirst)
        {
            editor.BreakBefore(subquery.Open, indent);
        }

        if (!innerFirst)
        {
            editor.BreakBefore(firstInner, innerIndent);
        }

        var firstInnerLine = document.TokenLine(firstInner);
        var originalColumn = document.TokenColumn(firstInner);

        editor.MoveBlock(innerFirst ? firstInnerLine : firstInnerLine + 1, document.EndLine(subquery.Query), originalColumn, innerIndent);

        if (!closeFirst)
        {
            editor.BreakBefore(subquery.Close, indent);

            return;
        }

        editor.MoveBlock(closeLine, closeLine, document.TokenColumn(subquery.Close), indent);
    }

    /// <summary>
    /// A subquery in the first predicate of a WHERE or HAVING line lines up with the predicates that follow, which
    /// start after the clause keyword.
    /// </summary>
    private int PredicateOffset(int line)
    {
        var first = document.FirstTokenOnLine(line);

        if (first.TokenType is TSqlTokenType.Where or TSqlTokenType.Having)
        {
            return SqlLayoutDocument.TextOf(first).Length + 1;
        }

        return 0;
    }

    private readonly record struct Subquery(int Open, int Close, QueryExpression Query);
}
