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
            ["nolock-in-transaction"] = new("nolock-in-transaction", "NOLOCK does not make a transaction safe from blocking or consistency anomalies.", "Choose the isolation level deliberately and avoid using NOLOCK as transaction error handling.", "warning"),
            ["read-uncommitted"] = new("read-uncommitted", "READ UNCOMMITTED allows dirty and non-repeatable reads.", "Use the weakest isolation level only when the resulting inconsistency is acceptable.", "warning"),
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
            ["dml-cte-target"] = new("dml-cte-target", "The DML target is a common table expression rather than a directly writable object.", "Verify that the CTE is updatable and that the intended base table is targeted.", "warning"),
            ["dml-derived-target"] = new("dml-derived-target", "The DML target is a derived source whose write semantics are restricted.", "Target the base table explicitly or confirm that the derived target is writable.", "warning"),
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
            ["procedure-parameter-type"] = new("procedure-parameter-type", "A procedure argument literal does not match the declared parameter type.", "Pass a value with the declared type or cast it explicitly after checking the conversion.", "warning"),
            ["too-many-procedure-arguments"] = new("too-many-procedure-arguments", "A procedure call supplies more arguments than the declaration accepts.", "Remove the extra arguments or update the procedure contract intentionally.", "error"),
            ["missing-procedure-parameter"] = new("missing-procedure-parameter", "A procedure call omits a required parameter.", "Pass the parameter explicitly or confirm that the procedure declaration supplies a default.", "error"),
            ["unknown-procedure-parameter"] = new("unknown-procedure-parameter", "A named procedure argument is not declared by the target procedure.", "Check the parameter spelling and refresh the index if the procedure changed.", "error"),
            ["invalid-procedure-default"] = new("invalid-procedure-default", "DEFAULT was supplied for a procedure parameter without a declared default.", "Pass an explicit value or add a valid default to the procedure declaration.", "error"),
            ["procedure-output-ignored"] = new("procedure-output-ignored", "A procedure OUTPUT parameter is called without a writable caller variable.", "Pass a variable and mark the argument OUTPUT when the returned value is needed.", "warning"),
            ["unknown-procedure"] = new("unknown-procedure", "The procedure is not present in the workspace index.", "Check the object name or reindex the workspace; external database objects may be unavailable.", "warning"),
            ["unknown-function"] = new("unknown-function", "The function is not present in the workspace index.", "Check the object name or reindex the workspace; built-in and external functions may be unavailable.", "warning"),
            ["function-argument-count"] = new("function-argument-count", "A function call does not match the indexed function signature.", "Check the argument count and overload or function definition.", "error"),
            ["unknown-table"] = new("unknown-table", "The table or view is not present in the workspace index.", "Check the object name or refresh the index; external database objects may be unavailable.", "warning"),
            ["transaction-without-error-handling"] = new("transaction-without-error-handling", "The transaction has no visible error-handling path.", "Use TRY/CATCH with an explicit rollback policy around the transaction.", "warning"),
            ["merge-nondeterministic-match"] = new("merge-nondeterministic-match", "A MERGE match can target the same row more than once.", "Make the match key unique or split the operation into deterministic statements.", "warning"),
            ["merge"] = new("merge", "MERGE combines insert, update, and delete branches with complex matching semantics.", "Verify concurrency, uniqueness, and branch behavior; separate statements may be easier to reason about.", "warning"),
            ["merge-top-without-order"] = new("merge-top-without-order", "TOP limits MERGE rows without defining which rows are selected.", "Add an ordering strategy or remove TOP when deterministic batching is required.", "warning"),
            ["foreign-key-join-mismatch"] = new("foreign-key-join-mismatch", "A JOIN does not use the indexed foreign-key relationship as expected.", "Verify the join columns and data types against the declared relationship.", "warning"),
            ["join-type-mismatch"] = new("join-type-mismatch", "JOIN columns use incompatible or widening data types.", "Align the data types at the schema boundary or cast deliberately after checking the plan.", "warning"),
            ["precision-loss"] = new("precision-loss", "A numeric value may lose precision when assigned or compared.", "Choose a target type with sufficient precision and scale.", "warning"),
            ["computed-insert"] = new("computed-insert", "An INSERT targets a computed column that SQL Server calculates.", "Remove the computed column from the INSERT target list.", "error"),
            ["identity-insert"] = new("identity-insert", "An INSERT supplies a value for an identity column.", "Omit the identity column or enable IDENTITY_INSERT only for a controlled migration.", "warning"),
            ["nullable-source-insert"] = new("nullable-source-insert", "An INSERT SELECT source may produce NULL for a required target column.", "Add a NULL-safe expression or validate the source before inserting.", "warning"),
            ["missing-required-insert-column"] = new("missing-required-insert-column", "An INSERT omits a required target column.", "Supply a value or confirm that a default or identity rule fills it.", "error"),
            ["unknown-insert-column"] = new("unknown-insert-column", "An INSERT names a column that is not known on the target.", "Check the spelling and refresh the index if the target schema changed.", "error"),
            ["unknown-update-column"] = new("unknown-update-column", "An UPDATE assigns a column that is not known on the target.", "Check the spelling and target alias.", "error"),
            ["duplicate-insert-column"] = new("duplicate-insert-column", "An INSERT lists the same target column more than once.", "Keep one assignment for each target column.", "error"),
            ["duplicate-update-column"] = new("duplicate-update-column", "An UPDATE assigns the same target column more than once.", "Combine the assignments into one deterministic value.", "error"),
            ["duplicate-merge-column"] = new("duplicate-merge-column", "A MERGE action assigns the same column more than once.", "Keep one assignment per target column in each MERGE branch.", "error"),
            ["insert-value-count"] = new("insert-value-count", "An INSERT value list does not match its target column count.", "Align the target columns and values one-to-one.", "error"),
            ["insert-select-count"] = new("insert-select-count", "An INSERT SELECT projection does not match its target column count.", "Align the target columns and SELECT expressions one-to-one.", "error"),
            ["set-operation-column-count"] = new("set-operation-column-count", "A set operation combines queries with different column counts.", "Make every branch project the same number of columns in the same order.", "error"),
            ["count-column"] = new("count-column", "COUNT references a column where row-count semantics may be intended.", "Use COUNT(*) for rows or confirm that NULL filtering is intentional.", "warning"),
            ["ungrouped-select-column"] = new("ungrouped-select-column", "A grouped query selects a column that is neither grouped nor aggregated.", "Add the column to GROUP BY or aggregate it explicitly.", "error"),
            ["case-mixed-constants"] = new("case-mixed-constants", "CASE branches mix incompatible constant types.", "Use compatible branch types or cast the intended result type explicitly.", "warning"),
            ["top-zero"] = new("top-zero", "TOP (0) guarantees an empty result set.", "Remove the dead query or use a positive limit when rows are expected.", "info"),
            ["negative-offset"] = new("negative-offset", "OFFSET cannot be negative.", "Use zero or a positive offset.", "error"),
            ["invalid-fetch"] = new("invalid-fetch", "FETCH requires a positive row count.", "Use a positive FETCH count.", "error"),
            ["cte-alias-shadowing"] = new("cte-alias-shadowing", "A table alias shadows a CTE with the same name.", "Rename the alias or qualify the intended CTE explicitly.", "warning"),
            ["duplicate-alias"] = new("duplicate-alias", "A query scope declares the same table alias more than once.", "Use unique aliases within each query scope.", "error"),
            ["unbound-delete-target"] = new("unbound-delete-target", "The DELETE target cannot be resolved to a table or alias.", "Use the target alias declared in the DELETE FROM sources.", "error"),
            ["ambiguous-table"] = new("ambiguous-table", "An unqualified table name exists under multiple schemas.", "Qualify the table with the intended schema.", "error"),
            ["duplicate-definition"] = new("duplicate-definition", "The same object name has multiple indexed definitions.", "Keep one authoritative definition or qualify the source of each definition.", "warning"),
            ["parse-error"] = new("parse-error", "ScriptDom could not parse this SQL batch.", "Fix the syntax near the reported location; later dependencies may be incomplete.", "error"),
        };

    public static bool TryGet(string rule, out DiagnosticExplanation explanation)
    {
        return Explanations.TryGetValue(rule, out explanation!);
    }
}
