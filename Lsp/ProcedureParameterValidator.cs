using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Checks procedure and function calls against signatures available in the workspace index.</summary>
internal static class ProcedureParameterValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        IEnumerable<DatabaseObject> objects,
        IEnumerable<ObjectParameter> parameters
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
            findings.AddRange(LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine));
        }

        return findings;
    }

    private static IReadOnlyDictionary<string, string[]> NamesByBaseName(IEnumerable<DatabaseObject> objects, string kind) => objects
        .Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase))
        .GroupBy(item => item.Name.Split('.').Last(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);

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
            ReportAmbiguousName(procedure.BaseIdentifier.Value, procedure.SchemaIdentifier is null, proceduresByBaseName, node, "procedure");

            if (!procedures.Contains(procedureName))
            {
                ReportUnknownProcedure(procedureName, node);

                return;
            }

            var knownParameters = parametersByProcedure.GetValueOrDefault(procedureName) ?? [];
            var namedParameters = node.Parameters
                .Where(parameter => parameter.Variable is not null)
                .ToArray();
            ReportUnknownNamedParameters(procedureName, knownParameters, namedParameters);
            ReportExcessArguments(procedureName, knownParameters, node);
            ReportMissingRequiredParameters(procedureName, knownParameters, namedParameters, node);
            ReportIgnoredOutputParameters(procedureName, knownParameters, node);
        }

        /// <summary>Reports output parameters whose value is not requested by the call site.</summary>
        private void ReportIgnoredOutputParameters(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call
        )
        {
            var positionalIndex = 0;
            foreach (var argument in call.Parameters)
            {
                var parameter = argument.Variable is not null
                    ? knownParameters.FirstOrDefault(item => string.Equals(item.Name, argument.Variable.Name, StringComparison.OrdinalIgnoreCase))
                    : positionalIndex < knownParameters.Count ? knownParameters[positionalIndex] : null;

                if (argument.Variable is null)
                {
                    positionalIndex++;
                }

                if (parameter?.IsOutput == true && !argument.IsOutput)
                {
                    Add("procedure-output-ignored", "info", argument, $"Procedure '{procedureName}' returns output parameter '{parameter.Name}', but this argument is not marked OUTPUT.");
                }
            }
        }

        private void ReportUnknownProcedure(string procedureName, ExecutableProcedureReference call)
        {
            if (!procedureName.Contains('.', StringComparison.Ordinal)
                || IsSystemProcedure(procedureName))
            {
                return;
            }

            Add("unknown-procedure", "warning", call, $"Procedure '{procedureName}' was not found in the workspace index.");
        }

        private static bool IsSystemProcedure(string procedureName) =>
            procedureName.StartsWith("dbo.sp_", StringComparison.OrdinalIgnoreCase)
            || procedureName.StartsWith("dbo.xp_", StringComparison.OrdinalIgnoreCase)
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

        private void ReportMissingRequiredParameters(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            IEnumerable<ExecuteParameter> namedParameters,
            ExecutableProcedureReference call
        )
        {
            var positionalCount = call.Parameters.TakeWhile(parameter => parameter.Variable is null).Count();
            var suppliedNames = namedParameters.Select(parameter => parameter.Variable.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in knownParameters.Where(parameter => IsMissingRequiredParameter(parameter, positionalCount, suppliedNames)))
            {
                Add("missing-procedure-parameter", call, $"Procedure '{procedureName}' requires parameter '{required.Name}'.");
            }
        }

        private static bool IsMissingRequiredParameter(
            ObjectParameter parameter,
            int positionalCount,
            IReadOnlySet<string> suppliedNames
        )
        {
            return parameter.DefaultValue is null
                && parameter.Ordinal > positionalCount
                && !suppliedNames.Contains(parameter.Name);
        }

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
                ReportUnknownFunction(node, functionName);

                return;
            }

            var parameterCount = parametersByProcedure.GetValueOrDefault(functionName)?.Count ?? 0;
            if (node.Parameters.Count != parameterCount)
            {
                Add("function-argument-count", node, $"Function '{functionName}' expects {parameterCount} argument(s), but this call supplies {node.Parameters.Count}.");
            }
        }

        private void ReportUnknownFunction(FunctionCall call, string functionName)
        {
            if (call.CallTarget is not MultiPartIdentifierCallTarget
                || IsSystemFunction(functionName))
            {
                return;
            }

            Add("unknown-function", "warning", call, $"Function '{functionName}' was not found in the workspace index.");
        }

        private void ReportAmbiguousFunction(FunctionCall call, string functionName)
        {
            var unqualified = call.CallTarget is not MultiPartIdentifierCallTarget;
            var baseName = functionName.Split('.').Last();
            ReportAmbiguousName(baseName, unqualified, functionsByBaseName, call, "function");
        }

        private void ReportAmbiguousName(
            string baseName,
            bool isUnqualified,
            IReadOnlyDictionary<string, string[]> namesByBaseName,
            TSqlFragment node,
            string kind
        )
        {
            if (!isUnqualified || !namesByBaseName.TryGetValue(baseName, out var candidates) || candidates.Length < 2)
            {
                return;
            }

            Add($"ambiguous-{kind}", "warning", node, $"The unqualified {kind} '{baseName}' exists under multiple schemas: {string.Join(", ", candidates)}.");
        }

        private static bool IsSystemFunction(string functionName) =>
            functionName.StartsWith("sys.", StringComparison.OrdinalIgnoreCase);

        private static string? FunctionName(FunctionCall node)
        {
            if (node.CallTarget is not MultiPartIdentifierCallTarget target)
            {
                return ObjectNames.StandardDefaultSchema + "." + node.FunctionName.Value;
            }

            var qualifiers = target.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value);
            return string.Join(".", qualifiers.Append(node.FunctionName.Value));
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
