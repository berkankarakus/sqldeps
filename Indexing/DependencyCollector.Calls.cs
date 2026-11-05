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

        if (!TryGetUserDefinedFunctionTarget(node, out var target))
        {
            return;
        }

        var schema = target.MultiPartIdentifier.Identifiers[^1].Value;
        AddDependency(schema + "." + node.FunctionName.Value, "CALL", node);
    }

    private static bool TryGetUserDefinedFunctionTarget(FunctionCall node, out MultiPartIdentifierCallTarget target)
    {
        target = null!;

        if (XmlMethods.Contains(node.FunctionName.Value))
        {
            return false;
        }

        if (node.CallTarget is not MultiPartIdentifierCallTarget multiPartTarget)
        {
            return false;
        }

        target = multiPartTarget;

        return true;
    }

    public override void Visit(SchemaObjectFunctionTableReference node) => AddDependency(Format(node.SchemaObject), "CALL", node);

    private void ReportDuplicateExecuteParameters(ExecutableProcedureReference node)
    {
        var parameterGroups = node.Parameters
            .Where(parameter => parameter.Variable is not null)
            .GroupBy(parameter => parameter.Variable!.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var parameters in parameterGroups.Where(group => group.Count() > 1))
        {
            AddLint(
                "duplicate-procedure-parameter",
                "error",
                parameters.First(),
                $"Procedure parameter '{parameters.Key}' is specified more than once."
            );
        }
    }

    private void ReportCountColumn(FunctionCall node)
    {
        if (TryGetCountedColumn(node, out var column))
        {
            AddLint("count-column", "info", node, $"COUNT({column}) excludes NULL values; use COUNT(*) when a row count is intended.");
        }
    }

    private static bool TryGetCountedColumn(FunctionCall node, out string column)
    {
        column = "";

        if (!IsCountFunction(node))
        {
            return false;
        }

        if (!TryGetSingleColumnParameter(node, out var reference))
        {
            return false;
        }

        if (!TryGetColumnName(reference, out column))
        {
            return false;
        }

        return column != "*";
    }

    private static bool IsCountFunction(FunctionCall node) => node.FunctionName.Value.Equals("COUNT", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSingleColumnParameter(FunctionCall node, out ColumnReferenceExpression reference)
    {
        reference = null!;

        if (node.Parameters.Count != 1)
        {
            return false;
        }

        if (node.Parameters[0] is not ColumnReferenceExpression column)
        {
            return false;
        }

        reference = column;

        return true;
    }
}
