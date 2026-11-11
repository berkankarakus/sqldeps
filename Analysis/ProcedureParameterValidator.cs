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

    private sealed partial class ProcedureVisitor(
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
