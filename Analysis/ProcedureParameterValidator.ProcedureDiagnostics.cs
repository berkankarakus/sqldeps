using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class ProcedureParameterValidator
{
    private sealed partial class ProcedureVisitor
    {
        private void ReportUnknownProcedure(string procedureName, bool unqualified, ExecutableProcedureReference call)
        {
            if (IsSystemProcedure(procedureName))
            {
                return;
            }

            Add("unknown-procedure", unqualified ? "info" : "warning", call, $"Procedure '{procedureName}' was not found in the workspace index.");
        }

        private static bool IsSystemProcedure(string procedureName) =>
            procedureName.StartsWith("dbo.sp_", StringComparison.OrdinalIgnoreCase)
            || procedureName.Contains(".dbo.sp_", StringComparison.OrdinalIgnoreCase)
            || procedureName.StartsWith("dbo.xp_", StringComparison.OrdinalIgnoreCase)
            || procedureName.Contains(".dbo.xp_", StringComparison.OrdinalIgnoreCase)
            || procedureName.StartsWith("sys.", StringComparison.OrdinalIgnoreCase);

        private void ReportUnknownNamedParameters(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            IEnumerable<ExecuteParameter> namedParameters)
        {
            var knownNames = knownParameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in namedParameters)
            {
                if (!knownNames.Contains(parameter.Variable.Name))
                {
                    Add("unknown-procedure-parameter", parameter, $"Procedure '{procedureName}' has no parameter named '{parameter.Variable.Name}'.");
                }
            }
        }

        private void ReportExcessArguments(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call)
        {
            if (call.Parameters.Count > knownParameters.Count)
            {
                Add("too-many-procedure-arguments", call, $"Procedure '{procedureName}' accepts at most {knownParameters.Count} argument(s), but this call supplies {call.Parameters.Count}.");
            }
        }

        private void ReportAmbiguousName(
            string baseName,
            bool isUnqualified,
            IReadOnlyDictionary<string, string[]> namesByBaseName,
            TSqlFragment node,
            string kind)
        {
            if (!TryGetAmbiguousCandidates(baseName, isUnqualified, namesByBaseName, out string[] candidates))
            {
                return;
            }

            Add($"ambiguous-{kind}", "warning", node, $"The unqualified {kind} '{baseName}' exists under multiple schemas: {string.Join(", ", candidates)}.");
        }

        private static bool TryGetAmbiguousCandidates(
            string baseName,
            bool isUnqualified,
            IReadOnlyDictionary<string, string[]> namesByBaseName,
            out string[] candidates)
        {
            candidates = [];

            if (!isUnqualified)
            {
                return false;
            }

            if (!namesByBaseName.TryGetValue(baseName, out string[]? matchingCandidates))
            {
                return false;
            }

            candidates = matchingCandidates;

            return candidates.Length >= 2;
        }
    }
}
