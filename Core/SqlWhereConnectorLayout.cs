using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Places top-level WHERE connectors at the end of the preceding predicate line.</summary>
internal sealed class SqlWhereConnectorLayout : TSqlFragmentVisitor
{
    private readonly SqlLayoutDocument document;
    private readonly SqlTextEdits edits = new();

    private SqlWhereConnectorLayout(SqlLayoutDocument document)
    {
        this.document = document;
    }

    public static string? Apply(SqlLayoutDocument document)
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

        List<BooleanExpression> predicates = [];
        FlattenTopLevel(node.SearchCondition, predicates);

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

            var indent = new string(' ', document.OriginalIndent(document.TokenLine(connector)));
            edits.Replace(beforeStart, beforeLength, " ");
            edits.Replace(afterStart, afterLength, "\n" + indent);
        }
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
