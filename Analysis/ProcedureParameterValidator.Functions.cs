using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class ProcedureParameterValidator
{
    private sealed partial class ProcedureVisitor
    {
        public override void Visit(FunctionCall node)
        {
            var functionName = FunctionName(node);

            if (functionName is null)
            {
                return;
            }

            ReportAmbiguousFunction(node, functionName);

            if (!functions.Contains(functionName))
            {
                ReportUnknownFunction(node, functionName, node.CallTarget is not MultiPartIdentifierCallTarget);

                return;
            }

            var parameterCount = parametersByProcedure.GetValueOrDefault(functionName)?.Count ?? 0;

            if (node.Parameters.Count != parameterCount)
            {
                Add("function-argument-count", node, $"Function '{functionName}' expects {parameterCount} argument(s), but this call supplies {node.Parameters.Count}.");
            }
        }

        private void ReportUnknownFunction(FunctionCall call, string functionName, bool unqualified)
        {
            if (IsSystemFunction(functionName))
            {
                return;
            }

            Add("unknown-function", unqualified ? "info" : "warning", call, $"Function '{functionName}' was not found in the workspace index.");
        }

        private void ReportAmbiguousFunction(FunctionCall call, string functionName)
        {
            var unqualified = call.CallTarget is not MultiPartIdentifierCallTarget;
            var baseName = ObjectNames.BaseName(functionName);
            ReportAmbiguousName(baseName, unqualified, functionsByBaseName, call, "function");
        }

        private static bool IsSystemFunction(string functionName) =>
            functionName.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
            || SqlServerBuiltInFunctions.Contains(functionName);

        private static string? FunctionName(FunctionCall node)
        {
            if (node.CallTarget is not MultiPartIdentifierCallTarget target)
            {
                return ObjectNames.StandardDefaultSchema + "." + node.FunctionName.Value;
            }

            var qualifiers = target.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value);

            return string.Join(".", qualifiers.Append(node.FunctionName.Value));
        }
    }
}
