using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(IfStatement node)
    {
        var result = ConstantExpressionEvaluator.Evaluate(node.Predicate);
        if (result is null)
        {
            return;
        }

        if (result.Value)
        {
            if (node.ElseStatement is not null)
            {
                AddLint(
                    "unreachable-branch",
                    "info",
                    node.ElseStatement,
                    "IF condition is always true; the ELSE branch is unreachable."
                );
            }

            return;
        }

        AddLint(
            "unreachable-branch",
            "info",
            node.ThenStatement,
            "IF condition is always false; the THEN branch is unreachable."
        );
    }
}
