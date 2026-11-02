namespace SqlDeps.Domain;

/// <summary>Shared confidence and freshness metadata for CLI and LSP diagnostics.</summary>
public sealed record DiagnosticMetadata(string Source, string Confidence, bool IsStale);

public static class DiagnosticMetadataMapper
{
    public static DiagnosticMetadata For(string source, string? rule = null)
    {
        var stale = string.Equals(source, "stale-index", StringComparison.Ordinal);
        return new DiagnosticMetadata(source, stale ? "low" : rule is null ? "high" : ConfidenceFor(rule), stale);
    }

    private static string ConfidenceFor(string rule) => rule switch
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
