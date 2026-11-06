namespace SqlDeps.Domain;

/// <summary>Shared confidence and freshness metadata for CLI and LSP diagnostics.</summary>
public sealed record DiagnosticMetadata(
    string Source,
    string Confidence,
    bool IsStale,
    string? ConfidenceReason = null
);

public static class DiagnosticMetadataMapper
{
    public static DiagnosticMetadata For(string source, string? rule = null, string? severity = null)
    {
        var stale = string.Equals(source, "stale-index", StringComparison.Ordinal);
        if (stale)
        {
            return new DiagnosticMetadata(source, "low", true, "stale-index");
        }

        var confidence = rule is null ? "high" : ConfidenceFor(rule, severity);
        return new DiagnosticMetadata(source, confidence, false, ConfidenceReasonFor(rule, severity));
    }

    private static string ConfidenceFor(string rule, string? severity)
    {
        if (rule is "parse-error" or "partial-analysis")
        {
            return "low";
        }

        if (severity is "info" && rule is ("unknown-table" or "unknown-procedure" or "unknown-function"))
        {
            return "low";
        }

        return rule switch
        {
            "implicit-filter-conversion"
                or "implicit-conversion"
                or "leading-wildcard-like"
                or "non-sargable-function"
                or "merge"
                or "merge-nondeterministic-match"
                or "transaction-without-error-handling"
                or "nolock"
                or "nolock-in-transaction"
                or "unfiltered-select"
                or "insert-exec-count"
                or "unindexed-where"
                or "precision-loss"
                or "foreign-key-join-mismatch"
                or "unindexed-order-by"
                or "unindexed-join"
                or "join-type-mismatch"
                or "insert-exec-type"
                or "nullable-source-insert"
                or "dml-ineffective-filter"
                or "dml-always-false-filter"
                or "procedure-output-ignored" => "low",
            _ => "medium",
        };
    }

    private static string? ConfidenceReasonFor(string? rule, string? severity)
    {
        if (rule is "parse-error" or "partial-analysis")
        {
            return rule == "parse-error"
                ? "scriptdom-tsql160-parser"
                : "document-size-limit";
        }

        if (severity is "info" && rule is ("unknown-table" or "unknown-procedure" or "unknown-function"))
        {
            return "source-index-no-catalog";
        }

        return null;
    }
}
