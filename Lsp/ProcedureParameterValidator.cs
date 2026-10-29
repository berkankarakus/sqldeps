using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Checks procedure and function calls against signatures available in the workspace index.</summary>
internal static class ProcedureParameterValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        string text,
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
        var parametersByProcedure = parameters
            .GroupBy(parameter => parameter.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ObjectParameter>)group.OrderBy(parameter => parameter.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        List<LintFinding> findings = [];

        foreach (var batch in ParsedSqlBatches.From(text))
        {
            ProcedureVisitor visitor = new(sourceFile, batch.StartLine, procedures, functions, parametersByProcedure);
            batch.Script.Accept(visitor);
            findings.AddRange(LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine));
        }

        return findings;
    }

    private sealed class ProcedureVisitor(
        string sourceFile,
        int batchStartLine,
        HashSet<string> procedures,
        HashSet<string> functions,
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
            if (!procedures.Contains(procedureName))
            {
                return;
            }

            var knownParameters = parametersByProcedure.GetValueOrDefault(procedureName) ?? [];
            var knownParameterNames = knownParameters
                .Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var namedParameters = node.Parameters
                .Where(parameter => parameter.Variable is not null)
                .ToArray();
            foreach (var parameter in namedParameters)
            {
                var name = parameter.Variable.Name;
                if (!knownParameterNames.Contains(name))
                {
                    Add("unknown-procedure-parameter", parameter, $"Procedure '{procedureName}' has no parameter named '{name}'.");
                }
            }

            if (node.Parameters.Count > knownParameters.Count)
            {
                Add("too-many-procedure-arguments", node, $"Procedure '{procedureName}' accepts at most {knownParameters.Count} argument(s), but this call supplies {node.Parameters.Count}.");
            }

            var positionalCount = node.Parameters.TakeWhile(parameter => parameter.Variable is null).Count();
            var suppliedNames = namedParameters
                .Select(parameter => parameter.Variable.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in knownParameters.Where(parameter => parameter.DefaultValue is null && parameter.Ordinal > positionalCount && !suppliedNames.Contains(parameter.Name)))
            {
                Add("missing-procedure-parameter", node, $"Procedure '{procedureName}' requires parameter '{required.Name}'.");
            }
        }

        public override void Visit(FunctionCall node)
        {
            var functionName = FunctionName(node);
            if (functionName is null || !functions.Contains(functionName))
            {
                return;
            }

            var parameterCount = parametersByProcedure.GetValueOrDefault(functionName)?.Count ?? 0;
            if (node.Parameters.Count != parameterCount)
            {
                Add("function-argument-count", node, $"Function '{functionName}' expects {parameterCount} argument(s), but this call supplies {node.Parameters.Count}.");
            }
        }

        private static string? FunctionName(FunctionCall node)
        {
            if (node.CallTarget is not MultiPartIdentifierCallTarget target)
            {
                return ObjectNames.StandardDefaultSchema + "." + node.FunctionName.Value;
            }

            var qualifiers = target.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value);
            return string.Join(".", qualifiers.Append(node.FunctionName.Value));
        }

        private void Add(string rule, TSqlFragment node, string message)
        {
            Findings.Add(new LintFinding(
                rule,
                "error",
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
