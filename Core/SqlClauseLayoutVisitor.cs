using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// Starts the SELECT list on the line below SELECT when it has several elements, and starts the WHERE predicates
/// on the line below WHERE when there are several of them. Elements and predicates come from the syntax tree and
/// each one is placed at the same indentation, independent of how the generator aligned them; the lines inside an
/// element move with it, so nested subqueries keep their relative indentation.
/// </summary>
internal sealed class SqlClauseLayoutVisitor : TSqlFragmentVisitor
{
    private const int IndentSize = 4;

    private readonly SqlLayoutDocument document;
    private readonly SqlLayoutEditor editor;
    private bool changed;

    private SqlClauseLayoutVisitor(SqlLayoutDocument document)
    {
        this.document = document;
        editor = new SqlLayoutEditor(document);
    }

    public static string? Apply(SqlLayoutDocument document)
    {
        SqlClauseLayoutVisitor visitor = new(document);
        document.Script.Accept(visitor);

        if (!visitor.changed)
        {
            return null;
        }

        return visitor.editor.Build();
    }

    public override void Visit(QuerySpecification node)
    {
        if (node.SelectElements.Count < 2)
        {
            return;
        }

        var hasModifiers = node.TopRowFilter is not null || node.UniqueRowFilter != UniqueRowFilter.NotSpecified;
        var firstElement = hasModifiers ? node.SelectElements[0].FirstTokenIndex : document.NextCode(node.FirstTokenIndex);

        if (firstElement < 0)
        {
            return;
        }

        List<Entry> entries = [new Entry(firstElement, document.EndLine(node.SelectElements[0]))];

        for (var index = 1; index < node.SelectElements.Count; index++)
        {
            entries.Add(new Entry(node.SelectElements[index].FirstTokenIndex, document.EndLine(node.SelectElements[index])));
        }

        Layout(document.TokenLine(node.FirstTokenIndex), entries);
    }

    public override void Visit(WhereClause node)
    {
        if (node.SearchCondition is null || CountTopLevelPredicates(node.SearchCondition) < 2)
        {
            return;
        }

        // Some predicate nodes (EXISTS) do not own their leading keyword, so take the token after WHERE instead.
        var firstPredicate = document.NextCode(node.FirstTokenIndex);

        if (firstPredicate < 0)
        {
            return;
        }

        List<BooleanExpression> predicates = [];
        FlattenTopLevel(node.SearchCondition, predicates);

        List<Entry> entries = [new Entry(firstPredicate, document.EndLine(predicates[0]))];

        for (var index = 1; index < predicates.Count; index++)
        {
            var connector = document.NextCode(predicates[index - 1].LastTokenIndex);

            if (connector < 0 || document.Tokens[connector].TokenType is not (TSqlTokenType.And or TSqlTokenType.Or))
            {
                return;
            }

            entries.Add(new Entry(connector, document.EndLine(predicates[index])));
        }

        Layout(document.TokenLine(node.FirstTokenIndex), entries);
    }

    /// <summary>Places every entry one level in from the clause; comment lines between entries line up with them.</summary>
    private void Layout(int clauseLine, List<Entry> entries)
    {
        var indent = editor.FinalIndent(clauseLine) + IndentSize;
        var previousEndLine = clauseLine;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var startLine = document.TokenLine(entry.StartToken);
            var firstOnLine = document.IsFirstOnLine(entry.StartToken);

            for (var line = previousEndLine + 1; line < startLine; line++)
            {
                if (document.IsCommentOnlyLine(line))
                {
                    editor.SetIndent(line, indent);
                }
            }

            if (firstOnLine)
            {
                editor.MoveBlock(startLine, entry.EndLine, document.TokenColumn(entry.StartToken), indent);
            }
            else if (index == 0)
            {
                editor.BreakBefore(entry.StartToken, indent);
                editor.MoveBlock(startLine + 1, entry.EndLine, document.TokenColumn(entry.StartToken), indent);
            }

            previousEndLine = entry.EndLine;
        }

        changed = true;
    }

    /// <summary>The leaves of the top-level AND/OR chain, in order; a parenthesized group is one predicate.</summary>
    private static void FlattenTopLevel(BooleanExpression expression, List<BooleanExpression> leaves)
    {
        if (expression is BooleanBinaryExpression binary)
        {
            FlattenTopLevel(binary.FirstExpression, leaves);
            FlattenTopLevel(binary.SecondExpression, leaves);

            return;
        }

        leaves.Add(expression);
    }

    private static int CountTopLevelPredicates(BooleanExpression expression)
    {
        List<BooleanExpression> leaves = [];
        FlattenTopLevel(expression, leaves);

        return leaves.Count;
    }

    private readonly record struct Entry(int StartToken, int EndLine);
}
