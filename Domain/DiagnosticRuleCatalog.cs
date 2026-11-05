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
            ["implicit-conversion"] = new("implicit-conversion", "SQL Server may convert one side of a comparison implicitly.", "Align the compared data types explicitly and verify the access path.", "info"),
            ["implicit-filter-conversion"] = new("implicit-filter-conversion", "A filter literal may be converted to the column type at runtime.", "Use a correctly typed literal or an explicit CAST when the conversion is intentional.", "warning"),
            ["leading-wildcard-like"] = new("leading-wildcard-like", "A leading wildcard prevents a normal index seek from narrowing the search.", "Use a prefix-searchable predicate or a search strategy designed for contains semantics.", "warning"),
            ["non-sargable-function"] = new("non-sargable-function", "A function around a filtered column can prevent an index seek.", "Rewrite the predicate or use a persisted/indexed expression when justified.", "warning"),
            ["update-without-where"] = new("update-without-where", "The UPDATE can modify every row in its target.", "Add and verify a restrictive WHERE clause before execution.", "warning"),
            ["delete-without-where"] = new("delete-without-where", "The DELETE can remove every row in its target.", "Add and verify a restrictive WHERE clause before execution.", "warning"),
            ["dml-ineffective-filter"] = new("dml-ineffective-filter", "A DML filter is always true, so the statement can affect every target row.", "Review the predicate and confirm that broad modification is intentional.", "warning"),
            ["dml-always-false-filter"] = new("dml-always-false-filter", "A DML filter is always false, so the statement cannot affect a row.", "Remove dead DML or correct the predicate if rows were expected to match.", "info"),
            ["unknown-column"] = new("unknown-column", "A referenced column is not known in the current indexed scope.", "Check the spelling, alias, and source table; refresh the index if the schema changed.", "warning"),
            ["ambiguous-column"] = new("ambiguous-column", "The column name resolves to more than one source.", "Qualify the column with the intended table alias.", "error"),
            ["unbound-identifier"] = new("unbound-identifier", "A multipart reference uses an unknown table alias.", "Use the alias declared in the current query scope.", "error"),
            ["invalid-alias-reference"] = new("invalid-alias-reference", "A reference is being interpreted as an alias-qualified name.", "Check whether a comma or the intended table alias is missing.", "error"),
            ["table-name-after-alias"] = new("table-name-after-alias", "A table is referenced by its original name after an alias was declared.", "Use the declared alias consistently in this query scope.", "error"),
            ["unindexed-where"] = new("unindexed-where", "The filtered column has no known supporting key or index.", "Verify the access pattern and add or choose an appropriate index when justified.", "info"),
            ["unindexed-join"] = new("unindexed-join", "A JOIN column has no known supporting key or index.", "Verify the join path and index the participating key when justified.", "info"),
            ["unindexed-order-by"] = new("unindexed-order-by", "The ORDER BY column has no known supporting key or index.", "Verify the sort requirement and consider an index whose key order supports it.", "info"),
            ["order-by-ordinal"] = new("order-by-ordinal", "An ordinal in ORDER BY depends on the current SELECT list order.", "Use a named column or expression so the sort remains stable when the projection changes.", "warning"),
            ["distinct-order-by"] = new("distinct-order-by", "DISTINCT cannot sort by a column outside its result shape.", "Add the expression deliberately or remove the sort if it is not required.", "error"),
            ["nullability-violation"] = new("nullability-violation", "A possibly NULL value is assigned to a non-nullable target.", "Handle NULL explicitly or provide a value that satisfies the target constraint.", "warning"),
            ["nullable-source-insert"] = new("nullable-source-insert", "An INSERT SELECT source may produce NULL for a required target column.", "Add a NULL-safe expression or validate the source before inserting.", "warning"),
            ["procedure-parameter-type"] = new("procedure-parameter-type", "A procedure argument literal does not match the declared parameter type.", "Pass a value with the declared type or cast it explicitly after checking the conversion.", "warning"),
            ["too-many-procedure-arguments"] = new("too-many-procedure-arguments", "A procedure call supplies more arguments than the declaration accepts.", "Remove the extra arguments or update the procedure contract intentionally.", "error"),
            ["unknown-procedure"] = new("unknown-procedure", "The procedure is not present in the workspace index.", "Check the object name or reindex the workspace; external database objects may be unavailable.", "warning"),
            ["unknown-function"] = new("unknown-function", "The function is not present in the workspace index.", "Check the object name or reindex the workspace; built-in and external functions may be unavailable.", "warning"),
            ["unknown-table"] = new("unknown-table", "The table or view is not present in the workspace index.", "Check the object name or refresh the index; external database objects may be unavailable.", "warning"),
            ["transaction-without-error-handling"] = new("transaction-without-error-handling", "The transaction has no visible error-handling path.", "Use TRY/CATCH with an explicit rollback policy around the transaction.", "warning"),
            ["merge-nondeterministic-match"] = new("merge-nondeterministic-match", "A MERGE match can target the same row more than once.", "Make the match key unique or split the operation into deterministic statements.", "warning"),
            ["duplicate-definition"] = new("duplicate-definition", "The same object name has multiple indexed definitions.", "Keep one authoritative definition or qualify the source of each definition.", "warning"),
            ["parse-error"] = new("parse-error", "ScriptDom could not parse this SQL batch.", "Fix the syntax near the reported location; later dependencies may be incomplete.", "error"),
        };

    public static bool TryGet(string rule, out DiagnosticExplanation explanation)
    {

        return Explanations.TryGetValue(rule, out explanation!);
    }
}
