using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Places WHERE connectors at the end of the preceding predicate line, including parenthesized groups.</summary>
internal sealed class SqlWhereConnectorLayout : TSqlFragmentVisitor
{
    private readonly SqlLayoutDocument document;
    private readonly SqlTextEdits edits = new();

    private SqlWhereConnectorLayout(SqlLayoutDocument document)
    {
        this.document = document;
    }

    internal static string? Apply(SqlLayoutDocument document)
    {
        SqlWhereConnectorLayout visitor = new(document);
        document.Script.Accept(visitor);

        return visitor.edits.Apply(document.Text);
    }

    public override void Visit(WhereClause node)
    {
        if (node.SearchCondition is null)
        {
            return;
        }

        LayoutConnectors(node.SearchCondition);
    }

    private void LayoutConnectors(BooleanExpression expression, int? predicateIndent = null)
    {
        List<BooleanExpression> predicates = [];
        FlattenTopLevel(expression, predicates);

        for (var index = 1; index < predicates.Count; index++)
        {
            var previous = predicates[index - 1];
            var current = predicates[index];
            var connector = document.NextCode(previous.LastTokenIndex);

            if (connector < 0 || document.Tokens[connector].TokenType is not (TSqlTokenType.And or TSqlTokenType.Or))
            {
                continue;
            }

            if (!document.TryGetGap(previous.LastTokenIndex, connector, out var beforeStart, out var beforeLength))
            {
                continue;
            }

            var afterToken = document.NextSignificant(connector);
            var afterBoundary = afterToken >= 0
                && document.Tokens[afterToken].TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment
                ? afterToken
                : connector;

            if (!document.TryGetGap(afterBoundary, current.FirstTokenIndex, out var afterStart, out var afterLength))
            {
                continue;
            }

            var indent = new string(' ', predicateIndent ?? document.OriginalIndent(document.TokenLine(connector)));
            edits.Replace(beforeStart, beforeLength, " ");
            edits.Replace(afterStart, afterLength, "\n" + indent);
        }

        foreach (var predicate in predicates)
        {
            if (predicate is BooleanParenthesisExpression parenthesized)
            {
                LayoutParenthesizedGroup(parenthesized, predicateIndent);
            }
            else if (predicate is BooleanNotExpression negated
                && negated.Expression is BooleanParenthesisExpression negatedGroup)
            {
                LayoutParenthesizedGroup(negatedGroup, predicateIndent);
            }
        }
    }

    private void LayoutParenthesizedGroup(BooleanParenthesisExpression group, int? predicateIndent)
    {
        if (group.Expression is not (BooleanBinaryExpression or BooleanParenthesisExpression or BooleanNotExpression))
        {
            return;
        }

        if (!document.TryGetGap(group.FirstTokenIndex, group.Expression.FirstTokenIndex, out var afterOpenStart, out var afterOpenLength)
            || !document.TryGetGap(group.Expression.LastTokenIndex, group.LastTokenIndex, out var beforeCloseStart, out var beforeCloseLength))
        {
            return;
        }

        var groupIndent = predicateIndent ?? document.OriginalIndent(document.TokenLine(group.FirstTokenIndex));
        edits.Replace(afterOpenStart, afterOpenLength, "\n" + new string(' ', groupIndent + 4));
        edits.Replace(beforeCloseStart, beforeCloseLength, "\n" + new string(' ', groupIndent));
        LayoutConnectors(group.Expression, groupIndent + 4);
    }

    private static void FlattenTopLevel(BooleanExpression expression, List<BooleanExpression> predicates)
    {
        if (expression is BooleanBinaryExpression binary)
        {
            FlattenTopLevel(binary.FirstExpression, predicates);
            FlattenTopLevel(binary.SecondExpression, predicates);

            return;
        }

        predicates.Add(expression);
    }
}
