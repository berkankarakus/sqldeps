using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static string ExecuteDiagnosticExplanationCommand(JsonElement parameters)
    {
        if (!TryGetDiagnosticRuleArgument(parameters, out var rule))
        {
            throw new ArgumentException("sqldeps.explain-diagnostic requires a known diagnostic rule.");
        }

        if (!DiagnosticRuleCatalog.TryGet(rule, out var explanation))
        {
            throw new ArgumentException("sqldeps.explain-diagnostic requires a known diagnostic rule.");
        }

        var references = explanation.References is null
            ? ""
            : "\n\nEvidence:\n" + string.Join(
                "\n",
                explanation.References.Select(reference => $"- {reference.Title}: {reference.Note} ({reference.Url})"));

        return $"Diagnostic: {explanation.Rule}\n\n"
            + $"Why: {explanation.Summary}\n\n"
            + $"Suggested direction: {explanation.Guidance}"
            + references;
    }

    private static bool TryGetDiagnosticRuleArgument(JsonElement parameters, out string rule)
    {
        rule = "";

        if (!TryGetStringArguments(parameters, 1, out var values))
        {
            return false;
        }

        rule = values[0];

        return true;
    }
}
