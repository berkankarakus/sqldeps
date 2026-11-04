# FP/FN decisions

This file records boundaries that are intentionally outside the safe diagnostic subset. A new rule or
fixture must either add a deterministic corpus case or add a decision here before the coverage baseline
is changed.

| Boundary | Decision | Evidence |
| --- | --- | --- |
| Negative integer `OFFSET` represented as a unary ScriptDom expression | Included in the safe subset; report `negative-offset` with the unary literal range. | `fp_fn_negative_offset`, `fp_fn_negative_offset_safe` |
| Variable/parameter DML filters | Skip constant true/false diagnostics; runtime value is unavailable. | `fp_fn_dml_variable` |
| Subquery and nondeterministic DML filters | Skip constant evaluation; SQL three-valued/runtime semantics are not closed locally. | `lsp_subquery_dml_filter`, `fp_fn_dml_variable` |
| String-literal equality without collation metadata | Keep only collation-independent cases; do not infer server/database collation. | `lsp_dml_literal_collation` |
| Ambiguous implicit conversion | Report only explicit, known type-family mismatches; avoid guessing precedence or runtime conversion behavior. | `fp_fn_filter_positive`, `fp_fn_filter_negative`, `fp_fn_date_type_safe` |
| Parameterized dynamic SQL | Do not inspect runtime-expanded SQL; preserve the safe parameterized path without concatenation diagnostics. | `fp_fn_dynamic_sql_safe` |
| Stale workspace index | Return low-confidence stale diagnostics and reindex command actions; never offer text replacement from stale metadata. | `lsp_workspace_stale_output`, `quick_fix_stale_action` |
| Remote and correlated aliases | Treat an outer `OPENQUERY` alias and an enclosing query alias as valid only in the visible scope; do not parse the remote string as local SQL or leak aliases across sibling queries. | Local corpus reduced 41 remote-alias and 27 cross-scope `unbound-identifier` findings to 0; `fp_fn_openquery_alias`, `fp_fn_correlated_alias`, `fp_fn_alias_scope` |
| CTE and derived-table DML targets | Keep a warning instead of claiming that every CTE or derived target is invalid; updateability depends on the query shape. | Local corpus review bucket; `dml-cte-target`, `dml-derived-target` |
| Transactions without explicit error handling | Keep the warning when a transaction is not enclosed by a TRY/CATCH path that rolls back; sampled corpus procedures and jobs use unprotected transactions. | Local corpus review; `transaction-without-error-handling` |
| Parser input boundaries | Preserve parse errors and warnings for unsupported batches or non-SQL suffixes; do not silently turn them into lint findings. | Local corpus has 3 parse errors and 20 parse warnings |

## Rule-level matrix decisions

Rules with an explicit negative assertion in the corpus do not need a duplicate row here. The
remaining rules below have a documented safe boundary; the report fails if a new rule has neither a
negative assertion nor one of these entries.

| Rule | Negative boundary / decision | Evidence |
| --- | --- | --- |
| `ambiguous-table` | A single schema-resolved table is not ambiguous. | `lsp_ambiguous_table` |
| `ambiguous-update-target` | An update with one bound target alias is not ambiguous. | `lsp_invalid_alias_update` |
| `case-mixed-constants` | CASE branches with one constant type do not produce a conversion warning. | `lsp_case_mixed_constants` |
| `computed-insert` | Inserting into ordinary writable columns is allowed. | `lsp_insert_generated_columns` |
| `count-column` | `COUNT(*)` is the row-count form and is not warned on. | `lsp_count_column` |
| `cte-alias-shadowing` | A CTE alias not reused by a table reference is safe. | `lsp_cte_columns` |
| `distinct-order-by` | DISTINCT ordering by a selected column is valid. | `lsp_distinct_order_by` |
| `dml-cte-target` | A base-table UPDATE/DELETE is not a CTE target. | `fp_fn_derived_target_safe` |
| `duplicate-alias` | Distinct aliases in one query scope are valid. | `lsp_nested_alias` |
| `duplicate-insert-column` | A unique INSERT column list is valid. | `lsp_invalid_insert` |
| `duplicate-merge-column` | A MERGE SET list with unique assignments is valid. | `lsp_duplicate_merge_column` |
| `duplicate-procedure-parameter` | Named procedure arguments used once are valid. | `lsp_unknown_procedure_parameter` |
| `duplicate-update-column` | A unique UPDATE SET list is valid. | `lsp_invalid_update` |
| `foreign-key-join-mismatch` | A JOIN to the declared foreign-key target is valid. | `lsp_fk_join` |
| `function-argument-count` | A function called with its declared arity is valid. | `lsp_signature_help` |
| `identity-insert` | Omitting an identity column is the safe INSERT path. | `lsp_insert_generated_columns` |
| `insert-value-count` | Every VALUES row matching the target width is valid. | `lsp_insert_select_star` |
| `invalid-alias-reference` | A qualifier that names a real table alias is valid. | `lsp_invalid_alias_update` |
| `invalid-fetch` | A positive FETCH count is valid. | `quick_fix_paging` |
| `invalid-procedure-default` | Supplying a required procedure parameter is valid. | `lsp_signature_help` |
| `leading-wildcard-like` | A LIKE pattern that does not start with `%` is not warned on. | `lsp_unindexed_where` |
| `non-sargable-function` | Comparing the raw column without a wrapping function is safe. | `lsp_unindexed_where` |
| `not-in-null` | NOT IN lists without NULL preserve normal semantics. | `lsp_in_constant_dml_filter` |
| `null-comparison` | IS NULL/IS NOT NULL is the valid null-comparison form. | `quick_fix_null_fixed` |
| `nullable-source-insert` | A non-nullable source into a non-nullable target is safe. | `lsp_insert_select_nullability` |
| `order-by-ordinal` | An ordinal inside the SELECT-list range is valid. | `lsp_signature_help` |
| `procedure-parameter-type` | A literal convertible to the declared parameter type is valid. | `lsp_signature_help` |
| `read-uncommitted` | A table reference without READUNCOMMITTED has no hint warning. | `fp_fn_lint_safe` |
| `too-many-procedure-arguments` | A call at or below the declared arity is valid. | `lsp_signature_help` |
| `top-zero` | TOP with a positive count is valid. | `lsp_merge_top_safe` |
| `ungrouped-select-column` | Grouped aggregate queries selecting grouped columns are valid. | `lsp_grouped_aggregate` |
| `unindexed-join` | A JOIN key covered by a known index is valid. | `lsp_join_numeric_widening` |
| `unindexed-order-by` | ORDER BY on a known key/index column is valid. | `lsp_filtered_index_match` |
| `unindexed-where` | WHERE on a known key/index column is valid. | `lsp_filtered_index_match` |
| `unknown-function` | A function present in the indexed workspace is valid. | `lsp_signature_help` |
| `unknown-insert-column` | An INSERT column present in the target metadata is valid. | `lsp_insert_select_star` |
| `unknown-procedure` | A procedure present in the indexed workspace is valid. | `lsp_signature_help` |
| `unknown-procedure-parameter` | A named argument present in the procedure signature is valid. | `lsp_signature_help` |
| `unknown-update-column` | An UPDATE column present in the target metadata is valid. | `lsp_invalid_update` |
| `update-without-where` | An UPDATE with a restrictive WHERE clause is safe. | `quick_fix_where_fixed` |

The matrix report also exposes positive/negative assertion, range, and confidence counts per emitted rule:
`bash tests/smoke/fp-fn-report.sh`.
