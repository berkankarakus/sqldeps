using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Places CASE branches on separate lines while keeping the expression valid and idempotent.</summary>
internal sealed class SqlCaseLayoutVisitor : TSqlFragmentVisitor
{
    private readonly SqlLayoutDocument document;
    private readonly SqlLayoutEditor editor;
    private bool changed;

    private SqlCaseLayoutVisitor(SqlLayoutDocument document)
    {
        this.document = document;
        editor = new SqlLayoutEditor(document);
    }

    internal static string? Apply(SqlLayoutDocument document)
    {
        SqlCaseLayoutVisitor visitor = new(document);
        document.Script.Accept(visitor);

        if (!visitor.changed)
        {
            return null;
        }

        return visitor.editor.Build();
    }

    public override void Visit(SimpleCaseExpression node)
    {
        Layout(node.FirstTokenIndex, node.WhenClauses, node.ElseExpression);
    }

    public override void Visit(SearchedCaseExpression node)
    {
        Layout(node.FirstTokenIndex, node.WhenClauses, node.ElseExpression);
    }

    private void Layout(
        int caseToken,
        IEnumerable<WhenClause> whenClauses,
        ScalarExpression? elseExpression
    )
    {
        var clauses = whenClauses.ToArray();
        if (clauses.Length == 0)
        {
            return;
        }

        var caseIndent = document.TokenColumn(caseToken);
        var branchIndent = caseIndent + SqlLayoutStyle.IndentSize;
        List<Entry> entries = [];

        foreach (var whenClause in clauses)
        {
            entries.Add(
                new Entry(
                    whenClause.FirstTokenIndex,
                    document.EndLine(whenClause.ThenExpression),
                    branchIndent
                )
            );
        }

        if (elseExpression is not null)
        {
            var elseToken = document.PreviousCode(elseExpression.FirstTokenIndex);
            if (!IsKeyword(elseToken, "ELSE"))
            {
                return;
            }

            entries.Add(new Entry(elseToken, document.EndLine(elseExpression), branchIndent));
        }

        var endToken = document.NextCode(
            elseExpression?.LastTokenIndex ?? clauses[^1].ThenExpression.LastTokenIndex
        );
        if (!IsKeyword(endToken, "END"))
        {
            return;
        }

        entries.Add(new Entry(endToken, document.TokenLine(endToken), caseIndent));
        LayoutEntries(entries);
    }

    private void LayoutEntries(IReadOnlyList<Entry> entries)
    {
        var previousEndLine = -1;

        foreach (var entry in entries)
        {
            var startLine = document.TokenLine(entry.StartToken);
            var firstOnLine = document.IsFirstOnLine(entry.StartToken);

            if (previousEndLine >= 0)
            {
                for (var line = previousEndLine + 1; line < startLine; line++)
                {
                    if (document.IsCommentOnlyLine(line))
                    {
                        editor.SetIndent(line, entry.Indent);
                    }
                }
            }

            if (firstOnLine)
            {
                editor.MoveBlock(startLine, entry.EndLine, document.TokenColumn(entry.StartToken), entry.Indent);
            }
            else
            {
                editor.BreakBefore(entry.StartToken, entry.Indent);
                editor.MoveBlock(startLine + 1, entry.EndLine, document.TokenColumn(entry.StartToken), entry.Indent);
            }

            previousEndLine = entry.EndLine;
            changed = true;
        }
    }

    private bool IsKeyword(int tokenIndex, string keyword)
    {
        return tokenIndex >= 0
            && string.Equals(SqlLayoutDocument.TextOf(document.Tokens[tokenIndex]), keyword, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Entry(int StartToken, int EndLine, int Indent);
}
