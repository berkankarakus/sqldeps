using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Finds assignments that must be skipped in an unreachable constant branch.</summary>
internal sealed class ConstantVariableBranchAssignmentFinder : TSqlFragmentVisitor
{
    internal List<TSqlFragment> Statements { get; } = [];

    public override void Visit(DeclareVariableStatement node)
    {
        Statements.Add(node);
    }

    public override void Visit(SetVariableStatement node)
    {
        Statements.Add(node);
    }
}
