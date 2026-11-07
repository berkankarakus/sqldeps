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

        if (!parameters.TryGetProperty("arguments", out var arguments))
        {
            return false;
        }

        if (arguments.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (arguments.GetArrayLength() != 1)
        {
            return false;
        }

        if (arguments[0].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        rule = arguments[0].GetString() ?? "";

        return !string.IsNullOrWhiteSpace(rule);
    }
}
