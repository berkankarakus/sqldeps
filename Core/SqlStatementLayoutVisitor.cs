using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>
/// Reads the syntax tree and tidies what the script generator leaves behind: procedure parameters (one per line,
/// indented, spaces around the default value), INSERT ... VALUES rows aligned under the first row, and the alias or
/// target of MERGE and TOP statements joined onto the line above.
/// </summary>
internal sealed class SqlStatementLayoutVisitor : TSqlFragmentVisitor
{
    private const int IndentSize = 4;
    private const string ValuesKeyword = "VALUES ";

    private readonly SqlLayoutDocument document;
    private readonly SqlTextEdits edits = new();

    private SqlStatementLayoutVisitor(SqlLayoutDocument document)
    {
        this.document = document;
    }

    public static string? Apply(SqlLayoutDocument document)
    {
        SqlStatementLayoutVisitor visitor = new(document);
        document.Script.Accept(visitor);

        return visitor.edits.Apply(document.Text);
    }

    public override void Visit(CreateProcedureStatement node)
    {
        FormatParameters(node);
    }

    public override void Visit(AlterProcedureStatement node)
    {
        FormatParameters(node);
    }

    public override void Visit(CreateOrAlterProcedureStatement node)
    {
        FormatParameters(node);
    }

    public override void Visit(MergeSpecification node)
    {
        if (node.Target is null || node.TableAlias is null)
        {
            return;
        }

        var asToken = document.PreviousSignificant(node.TableAlias.FirstTokenIndex);

        if (asToken < 0 || document.Tokens[asToken].TokenType != TSqlTokenType.As)
        {
            return;
        }

        JoinWithSpace(node.Target.LastTokenIndex, asToken);
    }

    public override void Visit(DeleteSpecification node)
    {
        JoinTopWithTarget(node.TopRowFilter, node.Target);
    }

    public override void Visit(UpdateSpecification node)
    {
        JoinTopWithTarget(node.TopRowFilter, node.Target);
    }

    public override void Visit(ValuesInsertSource node)
    {
        if (node.RowValues.Count == 0)
        {
            return;
        }

        var valuesToken = document.PreviousSignificant(node.RowValues[0].FirstTokenIndex);

        if (valuesToken < 0 || document.Tokens[valuesToken].TokenType != TSqlTokenType.Values)
        {
            return;
        }

        var column = document.TokenColumn(valuesToken) + ValuesKeyword.Length;

        JoinWithSpace(valuesToken, node.RowValues[0].FirstTokenIndex);

        for (var index = 1; index < node.RowValues.Count; index++)
        {
            var previous = node.RowValues[index - 1].LastTokenIndex;
            var next = node.RowValues[index].FirstTokenIndex;

            if (document.TryGetGap(previous, next, out var start, out var length, TSqlTokenType.Comma))
            {
                edits.Replace(start, length, $",\n{new string(' ', column)}");
            }
        }
    }

    private void FormatParameters(ProcedureStatementBody node)
    {
        if (node.Parameters.Count == 0 || node.ProcedureReference is null)
        {
            return;
        }

        var first = node.Parameters[0];
        var last = node.Parameters[^1];

        if (!document.TryGetGap(node.ProcedureReference.LastTokenIndex, first.FirstTokenIndex, out var start, out _))
        {
            return;
        }

        var padding = new string(' ', document.TokenColumn(node.FirstTokenIndex) + IndentSize);
        List<string> lines = [];

        for (var index = 0; index < node.Parameters.Count; index++)
        {
            if (index > 0
                && !document.TryGetGap(node.Parameters[index - 1].LastTokenIndex, node.Parameters[index].FirstTokenIndex, out _, out _, TSqlTokenType.Comma))
            {
                return;
            }

            var separator = index < node.Parameters.Count - 1 ? "," : "";

            lines.Add($"{padding}{ParameterText(node.Parameters[index])}{separator}");
        }

        var end = last.StartOffset + last.FragmentLength;

        edits.Replace(start, end - start, "\n" + string.Join('\n', lines));
    }

    /// <summary>"@p INT=NULL" becomes "@p INT = NULL"; the rest of the parameter text is kept as written.</summary>
    private string ParameterText(ProcedureParameter parameter)
    {
        var start = parameter.StartOffset;
        var end = start + parameter.FragmentLength;

        if (parameter.Value is null)
        {
            return document.Text[start..end];
        }

        var equals = document.PreviousSignificant(parameter.Value.FirstTokenIndex);

        if (equals < 0 || document.Tokens[equals].TokenType != TSqlTokenType.EqualsSign)
        {
            return document.Text[start..end];
        }

        var beforeEquals = document.Text[start..document.Tokens[equals].Offset].TrimEnd();
        var value = document.Text[parameter.Value.StartOffset..end];

        return $"{beforeEquals} = {value}";
    }

    private void JoinTopWithTarget(TopRowFilter? top, TableReference? target)
    {
        if (top is null || target is null)
        {
            return;
        }

        JoinWithSpace(top.LastTokenIndex, target.FirstTokenIndex);
    }

    /// <summary>Replaces plain white space between two tokens (when it spans lines or is not one space) by one space.</summary>
    private void JoinWithSpace(int fromToken, int toToken)
    {
        if (!document.TryGetGap(fromToken, toToken, out var start, out var length))
        {
            return;
        }

        var gap = document.Text.Substring(start, length);

        if (gap != " ")
        {
            edits.Replace(start, length, " ");
        }
    }
}
