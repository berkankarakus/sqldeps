namespace SqlDeps.Domain;

/// <summary>A named diagnostic rule set used by CLI and protocol adapters.</summary>
public sealed record DiagnosticProfile(
    string Name,
    string Description,
    IReadOnlySet<string>? IncludedRules)
{
    /// <summary>Returns whether a rule is included in this profile.</summary>
    public bool Includes(string rule)
    {
        return IncludedRules is null || IncludedRules.Contains(rule);
    }
}

/// <summary>Built-in diagnostic profiles shared by automation and interactive clients.</summary>
public static class DiagnosticProfileCatalog
{
    private static readonly DiagnosticProfile Default = new(
        "default",
        "All indexed diagnostics.",
        null);

    private static readonly DiagnosticProfile Security = new(
        "security",
        "Injection, unsafe data access, and broad-write diagnostics.",
        Rules(
            "parse-error",
            "dynamic-sql",
            "dynamic-sql-concatenation",
            "null-comparison",
            "not-in-null",
            "nolock",
            "nolock-in-transaction",
            "read-uncommitted",
            "update-without-where",
            "delete-without-where",
            "dml-ineffective-filter",
            "dml-derived-target",
            "dml-cte-target",
            "identity-insert",
            "computed-insert",
            "nullable-source-insert",
            "unbound-delete-target",
            "unbound-identifier",
            "invalid-alias-reference",
            "unknown-column",
            "unknown-table"));

    private static readonly DiagnosticProfile Performance = new(
        "performance",
        "Access-path, conversion, projection, and query-shape diagnostics.",
        Rules(
            "parse-error",
            "unindexed-where",
            "unindexed-join",
            "unindexed-order-by",
            "leading-wildcard-like",
            "non-sargable-function",
            "implicit-conversion",
            "implicit-filter-conversion",
            "select-star",
            "unfiltered-select",
            "select-always-false-filter",
            "select-ineffective-filter",
            "join-ineffective-condition",
            "recursive-cte",
            "order-by-ordinal",
            "foreign-key-join-mismatch",
            "join-type-mismatch",
            "insert-select-type",
            "update-assignment-type",
            "count-column",
            "merge-nondeterministic-match",
            "precision-loss"));

    private static readonly IReadOnlyDictionary<string, DiagnosticProfile> Profiles =
        new Dictionary<string, DiagnosticProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [Default.Name] = Default,
            [Security.Name] = Security,
            [Performance.Name] = Performance,
        };

    /// <summary>Returns all supported profiles in stable display order.</summary>
    public static IReadOnlyList<DiagnosticProfile> All { get; } =
        [Default, Security, Performance];

    /// <summary>Looks up a profile, defaulting to the complete diagnostic set.</summary>
    public static DiagnosticProfile Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Default;
        }

        if (Profiles.TryGetValue(name, out DiagnosticProfile? profile))
        {
            return profile;
        }

        throw new ArgumentException(
            $"Unknown diagnostic profile: {name}. Available profiles: {string.Join(", ", Profiles.Keys.OrderBy(value => value))}");
    }

    private static IReadOnlySet<string> Rules(params string[] rules)
    {
        return rules.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
