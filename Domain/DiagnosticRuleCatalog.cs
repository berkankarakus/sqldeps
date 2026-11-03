namespace SqlDeps.Domain;

public sealed record DiagnosticExplanation(
    string Rule,
    string Summary,
    string Guidance,
    string DefaultSeverity
);

public static class DiagnosticRuleCatalog
{
    private static readonly IReadOnlyDictionary<string, DiagnosticExplanation> Explanations =
        new Dictionary<string, DiagnosticExplanation>(StringComparer.OrdinalIgnoreCase)
        {
            ["select-star"] = new("select-star", "SELECT * depends on an implicit result shape.", "List only the columns the caller needs.", "info"),
            ["unfiltered-select"] = new("unfiltered-select", "The SELECT has no row-limiting filter.", "Confirm that reading the full matching set is intentional.", "info"),
            ["nolock"] = new("nolock", "NOLOCK can read uncommitted or inconsistent data.", "Use a transactionally appropriate isolation level unless dirty reads are explicitly acceptable.", "warning"),
            ["dynamic-sql-concatenation"] = new("dynamic-sql-concatenation", "Dynamic SQL is assembled by string concatenation.", "Use sp_executesql parameters for values and validate dynamic identifiers separately.", "warning"),
            ["null-comparison"] = new("null-comparison", "Comparisons with NULL do not behave like ordinary value comparisons.", "Use IS NULL or IS NOT NULL.", "warning"),
            ["not-in-null"] = new("not-in-null", "A NULL in a NOT IN set can make the predicate match no rows.", "Remove NULL values or use NOT EXISTS.", "warning"),
            ["update-without-where"] = new("update-without-where", "The UPDATE can modify every row in its target.", "Add and verify a restrictive WHERE clause before execution.", "warning"),
            ["delete-without-where"] = new("delete-without-where", "The DELETE can remove every row in its target.", "Add and verify a restrictive WHERE clause before execution.", "warning"),
            ["unknown-column"] = new("unknown-column", "A referenced column is not known in the current indexed scope.", "Check the spelling, alias, and source table; refresh the index if the schema changed.", "warning"),
            ["ambiguous-column"] = new("ambiguous-column", "The column name resolves to more than one source.", "Qualify the column with the intended table alias.", "error"),
            ["unbound-identifier"] = new("unbound-identifier", "A multipart reference uses an unknown table alias.", "Use the alias declared in the current query scope.", "error"),
            ["invalid-alias-reference"] = new("invalid-alias-reference", "A reference is being interpreted as an alias-qualified name.", "Check whether a comma or the intended table alias is missing.", "error"),
            ["table-name-after-alias"] = new("table-name-after-alias", "A table is referenced by its original name after an alias was declared.", "Use the declared alias consistently in this query scope.", "error"),
            ["unindexed-where"] = new("unindexed-where", "The filtered column has no known supporting key or index.", "Verify the access pattern and add or choose an appropriate index when justified.", "info"),
            ["unindexed-join"] = new("unindexed-join", "A JOIN column has no known supporting key or index.", "Verify the join path and index the participating key when justified.", "info"),
            ["unknown-procedure"] = new("unknown-procedure", "The procedure is not present in the workspace index.", "Check the object name or reindex the workspace; external database objects may be unavailable.", "warning"),
            ["unknown-function"] = new("unknown-function", "The function is not present in the workspace index.", "Check the object name or reindex the workspace; built-in and external functions may be unavailable.", "warning"),
            ["duplicate-definition"] = new("duplicate-definition", "The same object name has multiple indexed definitions.", "Keep one authoritative definition or qualify the source of each definition.", "warning"),
            ["parse-error"] = new("parse-error", "ScriptDom could not parse this SQL batch.", "Fix the syntax near the reported location; later dependencies may be incomplete.", "error"),
        };

    public static bool TryGet(string rule, out DiagnosticExplanation explanation)
    {

        return Explanations.TryGetValue(rule, out explanation!);
    }
}
