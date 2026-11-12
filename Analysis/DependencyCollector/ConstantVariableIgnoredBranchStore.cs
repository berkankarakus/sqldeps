using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Tracks assignment nodes that belong to branches known to be unreachable.</summary>
internal sealed class ConstantVariableIgnoredBranchStore
{
    private readonly HashSet<TSqlFragment> statements = [];

    internal bool Remove(TSqlFragment statement)
    {
        return statements.Remove(statement);
    }

    internal void AddRange(IEnumerable<TSqlFragment> branchStatements)
    {
        statements.UnionWith(branchStatements);
    }
}
