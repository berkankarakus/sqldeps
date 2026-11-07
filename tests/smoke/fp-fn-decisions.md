# FP/FN decisions

This file records boundaries that are intentionally outside the safe diagnostic subset. A new rule or
fixture must either add a deterministic corpus case or add a decision here before the coverage baseline
is changed.

## Current local-corpus snapshot

The 2026-11-07 index pass covered 4,397 SQL files. It produced 3 parser errors and 20 parser warnings. The
static index lint findings were concentrated in `nolock` (19,770), `unfiltered-select` (2,599),
`non-sargable-function` (1,669), `select-star` (1,484), `count-column` (1,107), and
`leading-wildcard-like` (807). The current index contains 27,908 findings in total. The sampled
`dml-cte-target`, `dml-derived-target`, dynamic SQL, transaction, MERGE TOP, and NULL-comparison findings were
actionable or intentionally review-only; no proven false positive was found in this pass.

The parser errors are grouped by message as one `PROCEDURE`, one `GO`, and one `ALTER` syntax error. The 20
parser warnings all report non-SQL text after a valid SQL prefix. These are parser-boundary cases, not evidence
that the corresponding SQL diagnostics are false positives.

The index lint table does not emit document-scope identifier diagnostics such as `unknown-column`,
`unbound-identifier`, or alias/scope findings. Subquery, table-variable, temp-table, and correlated-scope FP/FN
review therefore still requires a separate LSP document-analysis pass; index counts must not be treated as evidence
that those rules are covered.

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
| Parser input boundaries | Preserve parse errors and warnings for unsupported batches or non-SQL suffixes; do not silently turn them into lint findings. | Current local-corpus snapshot has 3 parse errors and 20 parse warnings; `tests/smoke/local-corpus-fp-fn.sh` groups them by message |
| Intent-dependent performance heuristics | Treat `unfiltered-select`, `select-star`, `count-column`, wildcard LIKE, non-sargable functions, and `TOP (0)` as review suggestions, not correctness failures. | Current local-corpus review buckets; intent is unavailable to the static index |
| Shape-dependent safety warnings | Keep CTE/derived DML, transaction handling, and MERGE TOP warnings reviewable; do not classify them as false positives without a query-shape check. | Current local-corpus review buckets; 61 CTE/derived DML findings and 179 transaction findings |
| Local-corpus static warning triage | Dynamic identifier concatenation remains fail-closed, transaction warnings require explicit error handling, MERGE TOP without ORDER BY is actionable, and the sampled NULL comparison is a true positive. No proven FP was found in this pass. | Current local-corpus snapshot and `tests/smoke/local-corpus-fp-fn.sh` review buckets |

Static index findings cannot prove false negatives because an absent finding has no ground-truth counterpart. FN
coverage therefore remains fixture-driven: the LSP FP/FN corpus already asserts positive and negative examples for
subquery, CTE, table-variable, temp-table, dynamic-identifier, and DML scope cases. Additional corpus-derived cases
should extend that fixture set rather than being inferred from index counts.

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
| `merge-top-without-order` | A MERGE source with TOP and no ORDER BY remains a nondeterminism warning; the selected source rows are not stable. | Local corpus review; `merge-top-without-order` |
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
