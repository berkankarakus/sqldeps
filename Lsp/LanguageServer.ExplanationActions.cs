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
    };

    private static object? DiagnosticReviewAction(LintDiagnostic diagnostic)
    {
        if (!ReviewOnlyRules.Contains(diagnostic.Rule)
            || !DiagnosticRuleCatalog.TryGet(diagnostic.Rule, out var explanation))
        {
            return null;
        }

        var title = $"Review sqldeps '{diagnostic.Rule}'";

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
