using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Parses and records dependencies found inside dynamic SQL.</summary>
public sealed partial class DependencyCollector
{
    private void CollectInside(TSqlScript script, string enclosingScope)
    {
        _scope = enclosingScope;

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                statement.Accept(this);
            }
        }
    }

    private static ScalarExpression? FirstParameterOf(ExecutableProcedureReference node)
    {
        if (node.Parameters.Count == 0)
        {
            return null;
        }

        return node.Parameters[0].ParameterValue;
    }

    private static bool IsDynamicSqlProcedure(SchemaObjectName name) => string.Equals(
        name.BaseIdentifier.Value,
        "sp_executesql",
        StringComparison.OrdinalIgnoreCase
    );
}
