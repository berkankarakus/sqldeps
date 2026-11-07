using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static readonly IReadOnlySet<string> ReviewOnlyRules = new HashSet<string>(StringComparer.Ordinal)
    {
        "nullability-violation",
        "nullable-source-insert",
        "distinct-order-by",
        "dml-ineffective-filter",
        "dml-always-false-filter",
        "transaction-without-error-handling",
        "merge",
        "merge-nondeterministic-match",
        "merge-top-without-order",
        "nolock",
        "nolock-in-transaction",
        "read-uncommitted",
    };

    private static IReadOnlyList<DiagnosticReference> ReferencesFor(string rule)
    {
        if (!DiagnosticRuleCatalog.TryGet(rule, out var explanation))
        {
            return [];
        }

        return explanation.References ?? [];
    }

    private static bool IsReviewOnlyRule(string rule) => ReviewOnlyRules.Contains(rule);

    private static object? ExplanationMetadataFor(string? rule)
    {
        if (rule is null)
        {
            return null;
        }

        if (!DiagnosticRuleCatalog.TryGet(rule, out var explanation))
        {
            return null;
        }

        return new
        {
            summary = explanation.Summary,
            guidance = explanation.Guidance,
            references = explanation.References ?? [],
        };
    }

    private static object? DiagnosticExplanationAction(LintDiagnostic diagnostic)
    {
        if (!DiagnosticRuleCatalog.TryGet(diagnostic.Rule, out var explanation))
        {
            return null;
        }

        var actionVerb = ReviewOnlyRules.Contains(diagnostic.Rule) ? "Review" : "Explain";
        var title = $"{actionVerb} sqldeps '{diagnostic.Rule}'";

        return new
        {
            title,
            kind = "quickfix",
            data = new
            {
                source = "sqldeps",
                confidence = diagnostic.Confidence,
                preview = true,
                writesFile = false,
                applicability = "The diagnostic must be recognized by the sqldeps rule catalog; this action does not edit the document.",
                generation = "explanation-only",
                references = explanation.References ?? [],
            },
            command = new
            {
                title,
                command = "sqldeps.explain-diagnostic",
                arguments = new[] { diagnostic.Rule },
            },
        };
    }
}
