using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Collects procedure and function-call dependencies.</summary>
public sealed partial class DependencyCollector
{
    public override void Visit(ExecutableProcedureReference node)
    {
        ReportDuplicateExecuteParameters(node);
        var procedureName = node.ProcedureReference?.ProcedureReference;

        if (procedureName?.Name is null)
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", node);

            return;
        }

        if (IsDynamicSqlProcedure(procedureName.Name))
        {
            CollectDynamicSql(FirstParameterOf(node), node);

            return;
        }

        AddDependency(Format(procedureName.Name), "EXEC", node);
    }

    public override void Visit(ExecutableStringList node) => CollectDynamicSql(node.Strings, node);

    public override void Visit(FunctionCall node)
    {
        ReportCountColumn(node);

        if (node.CallTarget is not MultiPartIdentifierCallTarget target || XmlMethods.Contains(node.FunctionName.Value))
        {
            return;
        }

        var schema = target.MultiPartIdentifier.Identifiers[^1].Value;
        AddDependency(schema + "." + node.FunctionName.Value, "CALL", node);
    }

    public override void Visit(SchemaObjectFunctionTableReference node) => AddDependency(Format(node.SchemaObject), "CALL", node);

    private void ReportDuplicateExecuteParameters(ExecutableProcedureReference node)
    {
        foreach (var parameters in node.Parameters.Where(parameter => parameter.Variable is not null).GroupBy(parameter => parameter.Variable.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            AddLint("duplicate-procedure-parameter", "error", parameters.First(), $"Procedure parameter '{parameters.Key}' is specified more than once.");
        }
    }

    private void ReportCountColumn(FunctionCall node)
    {
        if (node.FunctionName.Value.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
            && node.Parameters.Count == 1
            && node.Parameters[0] is ColumnReferenceExpression reference
            && TryGetColumnName(reference, out var column)
            && column != "*")
        {
            AddLint("count-column", "info", node, $"COUNT({column}) excludes NULL values; use COUNT(*) when a row count is intended.");
        }
    }
}
