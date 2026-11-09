using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Keeps a single SELECT element beside SELECT when the generator inserted a line break.</summary>
internal sealed class SqlSingleSelectLayout : TSqlFragmentVisitor
{
    private readonly SqlLayoutDocument document;
    private readonly SqlTextEdits edits = new();

    private SqlSingleSelectLayout(SqlLayoutDocument document)
    {
        this.document = document;
    }

    public static string? Apply(SqlLayoutDocument document)
    {
        SqlSingleSelectLayout visitor = new(document);
        document.Script.Accept(visitor);

        return visitor.edits.Apply(document.Text);
    }

    public override void Visit(QuerySpecification node)
    {
        if (node.SelectElements.Count != 1)
        {
            return;
        }

        var firstElement = node.SelectElements[0].FirstTokenIndex;
        var previous = document.PreviousCode(firstElement);
        if (previous < 0 || !document.TryGetGap(previous, firstElement, out var start, out var length))
        {
            return;
        }

        if (document.Text.Substring(start, length) != " ")
        {
            edits.Replace(start, length, " ");
        }
    }
}
