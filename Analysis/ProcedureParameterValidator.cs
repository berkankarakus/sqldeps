using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Checks procedure and function calls against signatures available in the workspace index.</summary>
internal static partial class ProcedureParameterValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        IEnumerable<DatabaseObject> objects,
        IEnumerable<ObjectParameter> parameters,
        string? fullDocumentText = null,
        string? repositoryRoot = null
    )
    {
        var procedures = objects
            .Where(item => string.Equals(item.Kind, "procedure", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functions = objects
            .Where(item => string.Equals(item.Kind, "function", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var proceduresByBaseName = NamesByBaseName(objects, "procedure");
        var functionsByBaseName = NamesByBaseName(objects, "function");
        var parametersByProcedure = parameters
            .GroupBy(parameter => parameter.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ObjectParameter>)group.OrderBy(parameter => parameter.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            ProcedureVisitor visitor = new(sourceFile, batch.StartLine, procedures, functions, proceduresByBaseName, functionsByBaseName, parametersByProcedure);
            batch.Script.Accept(visitor);
            findings.AddRange(LintSuppressions.Filter(
                visitor.Findings,
                batch.Text,
                batch.StartLine,
                fullDocumentText,
                repositoryRoot));
        }

        return findings;
    }

    private static IReadOnlyDictionary<string, string[]> NamesByBaseName(
        IEnumerable<DatabaseObject> objects,
        string kind
    )
    {
        var matchingObjects = objects
            .Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase));
        var namesByBaseName = matchingObjects
            .GroupBy(item => ObjectNames.BaseName(item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return namesByBaseName;
    }

    private sealed class ProcedureVisitor(
        string sourceFile,
        int batchStartLine,
        HashSet<string> procedures,
        HashSet<string> functions,
        IReadOnlyDictionary<string, string[]> proceduresByBaseName,
        IReadOnlyDictionary<string, string[]> functionsByBaseName,
        IReadOnlyDictionary<string, IReadOnlyList<ObjectParameter>> parametersByProcedure
    ) : TSqlFragmentVisitor
    {
        public List<LintFinding> Findings { get; } = [];

        public override void Visit(ExecutableProcedureReference node)
        {
            var procedure = node.ProcedureReference?.ProcedureReference?.Name;
            if (procedure is null)
            {
                return;
            }

            var procedureName = ObjectNames.Format(procedure, ObjectNames.StandardDefaultSchema);
            var unqualified = procedure.SchemaIdentifier is null;
            ReportAmbiguousName(procedure.BaseIdentifier.Value, unqualified, proceduresByBaseName, node, "procedure");

            if (!procedures.Contains(procedureName))
            {
                ReportUnknownProcedure(procedureName, unqualified, node);

                return;
            }

            var knownParameters = parametersByProcedure.GetValueOrDefault(procedureName) ?? [];
            var namedParameters = node.Parameters
                .Where(parameter => parameter.Variable is not null)
                .ToArray();
            ReportUnknownNamedParameters(procedureName, knownParameters, namedParameters);
            ReportExcessArguments(procedureName, knownParameters, node);
            ReportMissingRequiredParameters(procedureName, knownParameters, node);
            ReportInvalidDefaultArguments(procedureName, knownParameters, node);
            ReportIgnoredOutputParameters(procedureName, knownParameters, node);
            ReportLiteralTypeMismatches(procedureName, knownParameters, node);
        }

        /// <summary>Reports output parameters whose value is not requested by the call site.</summary>
        private void ReportIgnoredOutputParameters(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call
        )
        {
            foreach (var binding in BindArguments(knownParameters, call))
            {
                if (binding.Parameter?.IsOutput == true && !binding.Argument.IsOutput)
                {
                    Add("procedure-output-ignored", "info", binding.Argument, $"Procedure '{procedureName}' returns output parameter '{binding.Parameter.Name}', but this argument is not marked OUTPUT.");
                }
            }
        }

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
            IEnumerable<ExecuteParameter> namedParameters
        )
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

        private void ReportExcessArguments(string procedureName, IReadOnlyList<ObjectParameter> knownParameters, ExecutableProcedureReference call)
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
            string kind
        )
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
            out string[] candidates
        )
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

        private void Add(string rule, TSqlFragment node, string message) => Add(rule, "error", node, message);

        private void Add(string rule, string severity, TSqlFragment node, string message)
        {
            Findings.Add(new LintFinding(
                rule,
                severity,
                "(batch)",
                sourceFile,
                batchStartLine + node.StartLine - 1,
                node.StartColumn,
                Math.Max(1, node.FragmentLength),
                message
            ));
        }
    }
}
