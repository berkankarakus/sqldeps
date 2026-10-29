using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Checks named EXEC arguments against procedure signatures available in the workspace index.</summary>
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
        var parametersByProcedure = parameters
            .GroupBy(parameter => parameter.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        List<LintFinding> findings = [];

        foreach (var batch in BatchSplitter.Split(text))
        {
            TSql160Parser parser = new(initialQuotedIdentifiers: true);
            using StringReader reader = new(batch.Text);
            var fragment = parser.Parse(reader, out IList<ParseError> errors);
            if (errors.Count > 0 || fragment is not TSqlScript script)
            {
                continue;
            }

            ProcedureVisitor visitor = new(sourceFile, batch.StartLine, procedures, parametersByProcedure);
            script.Accept(visitor);
            findings.AddRange(LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine));
        }

        return findings;
    }

    private sealed class ProcedureVisitor(
        string sourceFile,
        int batchStartLine,
        HashSet<string> procedures,
        IReadOnlyDictionary<string, HashSet<string>> parametersByProcedure
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
            foreach (var parameter in node.Parameters.Where(parameter => parameter.Variable is not null))
            {
                var name = parameter.Variable.Name;
                if (!knownParameters.Contains(name))
                {
                    Add(parameter, $"Procedure '{procedureName}' has no parameter named '{name}'.");
                }
            }
        }

        private void Add(TSqlFragment node, string message)
        {
            Findings.Add(new LintFinding(
                "unknown-procedure-parameter",
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
