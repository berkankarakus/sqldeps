# ---- FP/FN acceptance corpus ----
fp_fn_constant_dml='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-constant-dml.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET title = \u0027x\u0027 WHERE 1 = 1;"}}}'
fp_fn_constant_dml_output="$(lsp_run "$lsp_initialize" "$fp_fn_constant_dml")"
[[ "$fp_fn_constant_dml_output" == *'"code":"dml-ineffective-filter"'* && "$fp_fn_constant_dml_output" == *'"confidence":"low"'* && "$fp_fn_constant_dml_output" == *'"start":{"line":0,"character":0}'* ]] || fail "FP/FN corpus keeps a deterministic DML true positive precise"

fp_fn_select_constant_true='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-select-constant-true.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE 1 = 1;"}}}'
fp_fn_select_constant_true_output="$(lsp_run "$lsp_initialize" "$fp_fn_select_constant_true")"
[[ "$fp_fn_select_constant_true_output" == *'"code":"select-ineffective-filter"'* && "$fp_fn_select_constant_true_output" == *'"confidence":"low"'* && "$fp_fn_select_constant_true_output" == *'"severity":3'* ]] || fail "FP/FN corpus reports always-true SELECT filters as low-confidence review findings"

fp_fn_select_constant_true_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-select-constant-true-safe.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id = @id;"}}}'
fp_fn_select_constant_true_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_select_constant_true_safe")"
[[ "$fp_fn_select_constant_true_safe_output" != *'"code":"select-ineffective-filter"'* ]] || fail "FP/FN corpus excludes variable SELECT filters from constant-true findings"

fp_fn_dml_variable='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-dml-variable.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET title = \u0027x\u0027 WHERE id = @id; DELETE FROM dbo.Info WHERE id IN (SELECT id FROM dbo.OtherInfo);"}}}'
fp_fn_dml_variable_output="$(lsp_run "$lsp_initialize" "$fp_fn_dml_variable")"
[[ "$fp_fn_dml_variable_output" != *'"code":"dml-ineffective-filter"'* && "$fp_fn_dml_variable_output" != *'"code":"dml-always-false-filter"'* ]] || fail "FP/FN corpus excludes variable and subquery DML filters"

fp_fn_filter_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-filter-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnNumbers (id INT); SELECT id FROM dbo.FpFnNumbers WHERE id = \u00271\u0027;"}}}'
fp_fn_filter_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_filter_positive")"
[[ "$fp_fn_filter_positive_output" == *'"code":"implicit-filter-conversion"'* && "$fp_fn_filter_positive_output" == *'"confidence":"low"'* && "$fp_fn_filter_positive_output" == *'"start":{"line":0,"character":81}'* && "$fp_fn_filter_positive_output" == *'"end":{"line":0,"character":84}'* ]] || fail "FP/FN corpus keeps a filter conversion true positive low confidence and precise"

fp_fn_filter_negative='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-filter-negative.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnNumbers (id INT); SELECT id FROM dbo.FpFnNumbers WHERE id = 1;"}}}'
fp_fn_filter_negative_output="$(lsp_run "$lsp_initialize" "$fp_fn_filter_negative")"
[[ "$fp_fn_filter_negative_output" != *'"code":"implicit-filter-conversion"'* ]] || fail "FP/FN corpus excludes compatible numeric filters"

fp_fn_negative_offset='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-negative-offset.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info ORDER BY id OFFSET -1 ROWS FETCH NEXT 1 ROWS ONLY;"}}}'
fp_fn_negative_offset_output="$(lsp_run "$lsp_initialize" "$fp_fn_negative_offset")"
[[ "$fp_fn_negative_offset_output" == *'"code":"negative-offset"'* && "$fp_fn_negative_offset_output" == *'"severity":1'* && "$fp_fn_negative_offset_output" == *'"start":{"line":0,"character":43}'* && "$fp_fn_negative_offset_output" == *'"end":{"line":0,"character":45}'* ]] || fail "FP/FN corpus reports unary negative OFFSET literals precisely"

fp_fn_negative_offset_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-negative-offset-safe.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info ORDER BY id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;"}}}'
fp_fn_negative_offset_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_negative_offset_safe")"
[[ "$fp_fn_negative_offset_safe_output" != *'"code":"negative-offset"'* ]] || fail "FP/FN corpus excludes non-negative OFFSET literals"

fp_fn_date_type_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-date-type-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDate (created_at DATETIME2(2), event_date DATE, rounded_at SMALLDATETIME); INSERT INTO dbo.FpFnDate (created_at, event_date, rounded_at) VALUES (\u00272026-10-30T12:34:56.123\u0027, \u00272026-10-30 12:00:00\u0027, \u00272026-10-30 12:00:01\u0027);"}}}'
fp_fn_date_type_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_date_type_positive")"
[[ "$fp_fn_date_type_positive_output" == *'"code":"implicit-conversion"'* && "$fp_fn_date_type_positive_output" == *'"code":"precision-loss"'* && "$fp_fn_date_type_positive_output" == *'"confidence":"low"'* && "$fp_fn_date_type_positive_output" == *'"start":{"line":0,"character":166}'* && "$fp_fn_date_type_positive_output" == *'"end":{"line":0,"character":191}'* ]] || fail "FP/FN corpus covers precise date/time literal conversion and precision loss"

fp_fn_date_type_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-date-type-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDateSafe (created_at DATETIME2(7)); INSERT INTO dbo.FpFnDateSafe (created_at) VALUES (CONVERT(DATETIME2(7), \u00272026-10-30T12:34:56.1234567\u0027));"}}}'
fp_fn_date_type_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_date_type_safe")"
[[ "$fp_fn_date_type_safe_output" != *'"code":"implicit-conversion"'* && "$fp_fn_date_type_safe_output" != *'"code":"precision-loss"'* ]] || fail "FP/FN corpus excludes explicit date/time conversions"

fp_fn_join_type_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-join-type-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnJoinInt (id INT); CREATE TABLE dbo.FpFnJoinDate (id DATE); SELECT i.id FROM dbo.FpFnJoinInt i JOIN dbo.FpFnJoinDate d ON i.id = d.id;"}}}'
fp_fn_join_type_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_join_type_positive")"
[[ "$fp_fn_join_type_positive_output" == *'"code":"join-type-mismatch"'* && "$fp_fn_join_type_positive_output" == *'"confidence":"low"'* && "$fp_fn_join_type_positive_output" == *'"start":{"line":0,"character":142}'* && "$fp_fn_join_type_positive_output" == *'"end":{"line":0,"character":146}'* ]] || fail "FP/FN corpus covers precise incompatible date/numeric JOIN types"

fp_fn_join_type_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-join-type-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnJoinIntSafe (id INT); CREATE TABLE dbo.FpFnJoinBigSafe (id BIGINT); SELECT i.id FROM dbo.FpFnJoinIntSafe i JOIN dbo.FpFnJoinBigSafe b ON i.id = b.id;"}}}'
fp_fn_join_type_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_join_type_safe")"
[[ "$fp_fn_join_type_safe_output" != *'"code":"join-type-mismatch"'* ]] || fail "FP/FN corpus excludes compatible numeric JOIN widening"

fp_fn_result_types='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-result-types.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnBig (value BIGINT); CREATE PROCEDURE dbo.FpFnValue AS SELECT CAST(1 AS INT) AS value; INSERT INTO dbo.FpFnBig (value) EXEC dbo.FpFnValue; CREATE TABLE dbo.FpFnText (value NVARCHAR(20)); CREATE PROCEDURE dbo.FpFnTextValue AS SELECT CAST(\u0027x\u0027 AS VARCHAR(20)) AS value; INSERT INTO dbo.FpFnText (value) EXEC dbo.FpFnTextValue;"}}}'
fp_fn_result_types_output="$(lsp_run "$lsp_initialize" "$fp_fn_result_types")"
[[ "$fp_fn_result_types_output" != *'"code":"insert-exec-type"'* ]] || fail "FP/FN corpus excludes compatible INSERT EXEC type families"

fp_fn_nullability='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-nullability.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDefault (id INT NOT NULL DEFAULT 1); INSERT INTO dbo.FpFnDefault DEFAULT VALUES; CREATE TABLE dbo.FpFnRequired (id INT NOT NULL); INSERT INTO dbo.FpFnRequired (id) VALUES (NULL);"}}}'
fp_fn_nullability_output="$(lsp_run "$lsp_initialize" "$fp_fn_nullability")"
[[ "$fp_fn_nullability_output" == *'"code":"nullability-violation"'* && "$fp_fn_nullability_output" != *'"code":"missing-required-insert-column"'* ]] || fail "FP/FN corpus distinguishes NULL violations from defaulted INSERTs"

fp_fn_lint_edges='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-lint-edges.sql","languageId":"sql","version":1,"text":"BEGIN TRANSACTION; SELECT * FROM dbo.Info WITH (NOLOCK); SELECT * FROM dbo.Info WITH (READUNCOMMITTED); COMMIT; DELETE FROM dbo.Info;"}}}'
fp_fn_lint_edges_output="$(lsp_run "$lsp_initialize" "$fp_fn_lint_edges")"
[[ "$fp_fn_lint_edges_output" == *'"code":"nolock"'* && "$fp_fn_lint_edges_output" == *'"code":"nolock-in-transaction"'* && "$fp_fn_lint_edges_output" == *'"code":"read-uncommitted"'* && "$fp_fn_lint_edges_output" == *'"code":"delete-without-where"'* ]] || fail "FP/FN corpus covers transaction and DELETE safety diagnostics"

fp_fn_single_nolock='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-single-nolock.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WITH (NOLOCK);"}}}'
fp_fn_single_nolock_output="$(lsp_run "$lsp_initialize" "$fp_fn_single_nolock")"
fp_fn_single_nolock_count="$(printf '%s' "$fp_fn_single_nolock_output" | grep -o '"code":"nolock"' | wc -l | tr -d ' ')"
[[ "$fp_fn_single_nolock_count" == "1" ]] || fail "FP/FN deduplicates one NOLOCK finding"

fp_fn_same_rule_distinct_columns='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-same-rule-distinct-columns.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WHERE name LIKE \u0027%first%\u0027 AND title LIKE \u0027%second%\u0027;"}}}'
fp_fn_same_rule_distinct_columns_output="$(lsp_run "$lsp_initialize" "$fp_fn_same_rule_distinct_columns")"
fp_fn_same_rule_distinct_columns_count="$(printf '%s' "$fp_fn_same_rule_distinct_columns_output" | grep -o '"code":"leading-wildcard-like"' | wc -l | tr -d ' ')"
[[ "$fp_fn_same_rule_distinct_columns_count" == "2" ]] || fail "FP/FN preserves same-rule findings at distinct columns"

fp_fn_lint_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-lint-safe.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WITH (NOLOCK) WHERE id = 1; DELETE FROM dbo.Info WHERE id = 1;"}}}'
fp_fn_lint_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_lint_safe")"
[[ "$fp_fn_lint_safe_output" == *'"code":"nolock"'* && "$fp_fn_lint_safe_output" != *'"code":"nolock-in-transaction"'* && "$fp_fn_lint_safe_output" != *'"code":"delete-without-where"'* ]] || fail "FP/FN corpus excludes transaction and filtered DELETE false positives"

fp_fn_set_operation='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-set-operation.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info UNION SELECT id, title FROM dbo.Info;"}}}'
fp_fn_set_operation_output="$(lsp_run "$lsp_initialize" "$fp_fn_set_operation")"
[[ "$fp_fn_set_operation_output" == *'"code":"set-operation-column-count"'* && "$fp_fn_set_operation_output" == *'"severity":1'* ]] || fail "FP/FN corpus covers set-operation column-count violations"

fp_fn_set_operation_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-set-operation-safe.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info UNION SELECT id FROM dbo.Info;"}}}'
fp_fn_set_operation_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_set_operation_safe")"
[[ "$fp_fn_set_operation_safe_output" != *'"code":"set-operation-column-count"'* ]] || fail "FP/FN corpus excludes equal-width set operations"

fp_fn_merge='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-merge.sql","languageId":"sql","version":1,"text":"MERGE dbo.Info AS target USING dbo.OtherInfo AS source ON target.id = source.id WHEN MATCHED THEN UPDATE SET target.title = source.title;"}}}'
fp_fn_merge_output="$(lsp_run "$lsp_initialize" "$fp_fn_merge")"
[[ "$fp_fn_merge_output" == *'"code":"merge"'* && "$fp_fn_merge_output" != *'"code":"merge-nondeterministic-match"'* ]] || fail "FP/FN corpus distinguishes deterministic MERGE matches"

fp_fn_merge_nondeterministic='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-merge-nondeterministic.sql","languageId":"sql","version":1,"text":"MERGE dbo.Info AS target USING dbo.OtherInfo AS source ON target.id > source.id WHEN MATCHED THEN UPDATE SET target.title = source.title;"}}}'
fp_fn_merge_nondeterministic_output="$(lsp_run "$lsp_initialize" "$fp_fn_merge_nondeterministic")"
[[ "$fp_fn_merge_nondeterministic_output" == *'"code":"merge"'* && "$fp_fn_merge_nondeterministic_output" == *'"code":"merge-nondeterministic-match"'* ]] || fail "FP/FN corpus covers nondeterministic MERGE matches"

fp_fn_derived_target='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-derived-target.sql","languageId":"sql","version":1,"text":"UPDATE derived SET id = 1 FROM (SELECT id FROM dbo.Info) derived;"}}}'
fp_fn_derived_target_output="$(lsp_run "$lsp_initialize" "$fp_fn_derived_target")"
[[ "$fp_fn_derived_target_output" == *'"code":"dml-derived-target"'* ]] || fail "FP/FN corpus covers derived DML targets"

fp_fn_ambiguous_delete='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-ambiguous-delete.sql","languageId":"sql","version":1,"text":"DELETE i FROM dbo.Info i JOIN dbo.OtherInfo i ON i.id = i.id;"}}}'
fp_fn_ambiguous_delete_output="$(lsp_run "$lsp_initialize" "$fp_fn_ambiguous_delete")"
[[ "$fp_fn_ambiguous_delete_output" == *'"code":"ambiguous-delete-target"'* ]] || fail "FP/FN corpus covers ambiguous DELETE targets"

fp_fn_delete_target_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-delete-target-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT); CREATE TABLE dbo.OtherInfo (id INT); DELETE i FROM dbo.Info i JOIN dbo.OtherInfo o ON i.id = o.id WHERE i.id = 1;"}}}'
fp_fn_delete_target_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_delete_target_safe")"
[[ "$fp_fn_delete_target_safe_output" != *'"code":"ambiguous-delete-target"'* && "$fp_fn_delete_target_safe_output" != *'"code":"unbound-delete-target"'* ]] || fail "FP/FN corpus excludes bound DELETE targets"

fp_fn_transaction_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-transaction-safe.sql","languageId":"sql","version":1,"text":"BEGIN TRY BEGIN TRANSACTION; UPDATE dbo.Info SET title = \u0027x\u0027 WHERE id = 1; COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; END CATCH"}}}'
fp_fn_transaction_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_transaction_safe")"
[[ "$fp_fn_transaction_safe_output" != *'"code":"transaction-without-error-handling"'* ]] || fail "FP/FN corpus accepts TRY/CATCH transactions with rollback"

fp_fn_merge_top='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-merge-top.sql","languageId":"sql","version":1,"text":"MERGE dbo.Info AS target USING (SELECT TOP (1) id FROM dbo.OtherInfo) AS source ON target.id = source.id WHEN MATCHED THEN UPDATE SET target.title = \u0027x\u0027;"}}}'
fp_fn_merge_top_output="$(lsp_run "$lsp_initialize" "$fp_fn_merge_top")"
[[ "$fp_fn_merge_top_output" == *'"code":"merge-top-without-order"'* && "$fp_fn_merge_top_output" == *'"severity":2'* ]] || fail "FP/FN corpus reports unordered TOP MERGE sources"
fp_fn_merge_top_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-merge-top-safe.sql","languageId":"sql","version":1,"text":"MERGE dbo.Info AS target USING (SELECT TOP (1) id FROM dbo.OtherInfo ORDER BY id) AS source ON target.id = source.id WHEN MATCHED THEN UPDATE SET target.title = \u0027x\u0027;"}}}'
fp_fn_merge_top_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_merge_top_safe")"
[[ "$fp_fn_merge_top_safe_output" != *'"code":"merge-top-without-order"'* ]] || fail "FP/FN corpus accepts ordered TOP MERGE sources"

fp_fn_derived_target_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-derived-target-safe.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET title = \u0027x\u0027 WHERE id = 1;"}}}'
fp_fn_derived_target_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_derived_target_safe")"
[[ "$fp_fn_derived_target_safe_output" != *'"code":"dml-derived-target"'* ]] || fail "FP/FN corpus excludes ordinary base-table DML targets"

fp_fn_dynamic_sql_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-dynamic-safe.sql","languageId":"sql","version":1,"text":"DECLARE @id INT = 1; EXEC sys.sp_executesql N\u0027SELECT * FROM dbo.Info WHERE id = @id\u0027, N\u0027@id INT\u0027, @id = @id;"}}}'
fp_fn_dynamic_sql_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_dynamic_sql_safe")"
[[ "$fp_fn_dynamic_sql_safe_output" != *'"code":"dynamic-sql-concatenation"'* ]] || fail "FP/FN corpus excludes parameterized dynamic SQL"

fp_fn_dynamic_identifier='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-dynamic-identifier.sql","languageId":"sql","version":1,"text":"DECLARE @table VARCHAR(50); EXEC(\u0027SELECT * FROM \u0027 + @table);"}}}'
fp_fn_dynamic_identifier_action='{"jsonrpc":"2.0","id":140,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-dynamic-identifier.sql"},"range":{"start":{"line":0,"character":33},"end":{"line":0,"character":58}},"context":{"diagnostics":[{"source":"sqldeps","code":"dynamic-sql-concatenation","range":{"start":{"line":0,"character":33},"end":{"line":0,"character":58}},"message":"Dynamic SQL is built by string concatenation; use parameters with sp_executesql for data values."}]}}}'
fp_fn_dynamic_identifier_output="$(lsp_run "$lsp_initialize" "$fp_fn_dynamic_identifier" "$fp_fn_dynamic_identifier_action")"
[[ "$fp_fn_dynamic_identifier_output" == *'"code":"dynamic-sql-concatenation"'* && "$fp_fn_dynamic_identifier_output" != *'Convert dynamic SQL to sp_executesql parameters'* ]] || fail "FP/FN corpus keeps dynamic object-name conversions disabled"

fp_fn_experimental_default='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-experimental-default.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT, title VARCHAR(10)); SELECT * FROM dbo.Info;"}}}'
fp_fn_experimental_default_action='{"jsonrpc":"2.0","id":141,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-experimental-default.sql"},"range":{"start":{"line":0,"character":58},"end":{"line":0,"character":59}},"context":{"diagnostics":[{"source":"sqldeps","code":"select-star","range":{"start":{"line":0,"character":58},"end":{"line":0,"character":59}},"message":"SELECT *"}]}}}'
fp_fn_experimental_default_output="$(lsp_run "$lsp_default_initialize" "$fp_fn_experimental_default" "$fp_fn_experimental_default_action")"
[[ "$fp_fn_experimental_default_output" != *'Expand SELECT * with indexed columns'* && "$fp_fn_experimental_default_output" != *'Expand SELECT * with qualified columns'* ]] || fail "FP/FN corpus keeps experimental SELECT-star generation opt-in"

fp_fn_derived_self_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-derived-self-scope.sql","languageId":"sql","version":1,"text":"SELECT t.rank_0_1 FROM (SELECT CASE WHEN col_f = 0 AND [rank] = 1 THEN [count] ELSE 0 END AS rank_0_1 FROM dbo.MissingSource) t;"}}}'
fp_fn_derived_self_scope_output="$(lsp_run "$lsp_initialize" "$fp_fn_derived_self_scope")"
[[ "$fp_fn_derived_self_scope_output" != *'Invalid column name \u0027col_f\u0027 on \u0027t\u0027'* && "$fp_fn_derived_self_scope_output" != *'Invalid column name \u0027rank\u0027 on \u0027t\u0027'* && "$fp_fn_derived_self_scope_output" != *'Invalid column name \u0027count\u0027 on \u0027t\u0027'* ]] || fail "FP/FN corpus excludes derived-table self-scope false positives"

fp_fn_derived_star_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-derived-star-scope.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDerivedStar (id INT, title VARCHAR(20)); SELECT derived.id FROM (SELECT source.* FROM dbo.FpFnDerivedStar AS source) AS derived;"}}}'
fp_fn_derived_star_scope_output="$(lsp_run "$lsp_initialize" "$fp_fn_derived_star_scope")"
[[ "$fp_fn_derived_star_scope_output" != *'"code":"unknown-column"'* ]] || fail "FP/FN corpus resolves derived-table star columns from indexed sources"

fp_fn_cte_star_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-cte-star-scope.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnCteStar (id INT, title VARCHAR(20)); WITH source_cte AS (SELECT source.* FROM dbo.FpFnCteStar AS source) SELECT source_cte.id FROM source_cte;"}}}'
fp_fn_cte_star_scope_output="$(lsp_run "$lsp_initialize" "$fp_fn_cte_star_scope")"
[[ "$fp_fn_cte_star_scope_output" != *'"code":"unknown-column"'* ]] || fail "FP/FN corpus resolves CTE star columns from indexed sources"

fp_fn_cte_star_scope_negative='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-cte-star-scope-negative.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnCteStarNegative (id INT); WITH source_cte AS (SELECT source.* FROM dbo.FpFnCteStarNegative AS source) SELECT source_cte.missing FROM source_cte;"}}}'
fp_fn_cte_star_scope_negative_output="$(lsp_run "$lsp_initialize" "$fp_fn_cte_star_scope_negative")"
[[ "$fp_fn_cte_star_scope_negative_output" == *'"code":"unknown-column"'* ]] || fail "FP/FN corpus preserves unknown-column diagnostics for unresolved CTE star columns"

fp_fn_derived_join_index_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-derived-join-index-scope.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDerivedJoin (col_a INT); CREATE INDEX IX_FpFnDerivedJoin_col_a ON dbo.FpFnDerivedJoin (col_a); SELECT u.col_a FROM dbo.FpFnDerivedJoin AS u JOIN (SELECT CAST(1 AS INT) AS col_a) AS t ON t.col_a = u.col_a;"}}}'
fp_fn_derived_join_index_scope_output="$(lsp_run "$lsp_initialize" "$fp_fn_derived_join_index_scope")"
[[ "$fp_fn_derived_join_index_scope_output" != *'JOIN compares \u0027t.col_a\u0027'* ]] || fail "FP/FN corpus excludes physical index warnings for derived-table aliases"

fp_fn_recursive_cte='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-recursive-cte.sql","languageId":"sql","version":1,"text":"WITH numbers AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM numbers) SELECT n FROM numbers;"}}}'
fp_fn_recursive_cte_output="$(lsp_run "$lsp_initialize" "$fp_fn_recursive_cte")"
[[ "$fp_fn_recursive_cte_output" == *'"code":"recursive-cte"'* && "$fp_fn_recursive_cte_output" == *'"severity":3'* ]] || fail "FP/FN corpus reports unguarded recursive CTEs as review-only"

fp_fn_recursive_cte_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-recursive-cte-safe.sql","languageId":"sql","version":1,"text":"WITH numbers AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM numbers WHERE n < 10) SELECT n FROM numbers;"}}}'
fp_fn_recursive_cte_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_recursive_cte_safe")"
[[ "$fp_fn_recursive_cte_safe_output" != *'"code":"recursive-cte"'* ]] || fail "FP/FN corpus excludes recursive CTEs with a visible guard"

fp_fn_cte_column_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-cte-column-count.sql","languageId":"sql","version":1,"text":"WITH invalid (id, title) AS (SELECT 1) SELECT * FROM invalid;"}}}'
fp_fn_cte_column_count_output="$(lsp_run "$lsp_initialize" "$fp_fn_cte_column_count")"
[[ "$fp_fn_cte_column_count_output" == *'"code":"cte-column-count"'* && "$fp_fn_cte_column_count_output" == *'"severity":1'* ]] || fail "FP/FN corpus reports mismatched CTE column declarations"

fp_fn_cte_column_count_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-cte-column-count-safe.sql","languageId":"sql","version":1,"text":"WITH valid (id, title) AS (SELECT 1, 2) SELECT * FROM valid;"}}}'
fp_fn_cte_column_count_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_cte_column_count_safe")"
[[ "$fp_fn_cte_column_count_safe_output" != *'"code":"cte-column-count"'* ]] || fail "FP/FN corpus excludes matching CTE column declarations"

fp_fn_constant_filter_and_branch_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-constant-filter-and-branch-safe.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id = @id; IF @flag = 1 BEGIN SELECT 1; END ELSE BEGIN SELECT 2; END"}}}'
fp_fn_constant_filter_and_branch_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_constant_filter_and_branch_safe")"
[[ "$fp_fn_constant_filter_and_branch_safe_output" != *'"code":"select-always-false-filter"'* && "$fp_fn_constant_filter_and_branch_safe_output" != *'"code":"unfiltered-select"'* && "$fp_fn_constant_filter_and_branch_safe_output" != *'"code":"unreachable-branch"'* ]] || fail "FP/FN corpus excludes variable filters and branches from constant-only findings"

fp_fn_join_constant_true='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-join-constant-true.sql","languageId":"sql","version":1,"text":"SELECT left_table.id FROM dbo.LeftTable AS left_table JOIN dbo.RightTable AS right_table ON 1 = 1;"}}}'
fp_fn_join_constant_true_output="$(lsp_run "$lsp_initialize" "$fp_fn_join_constant_true")"
[[ "$fp_fn_join_constant_true_output" == *'"code":"join-ineffective-condition"'* && "$fp_fn_join_constant_true_output" == *'"confidence":"low"'* && "$fp_fn_join_constant_true_output" == *'Cartesian-like'* ]] || fail "FP/FN corpus reports always-true JOIN conditions as review findings"

fp_fn_join_constant_true_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-join-constant-true-safe.sql","languageId":"sql","version":1,"text":"SELECT left_table.id FROM dbo.LeftTable AS left_table JOIN dbo.RightTable AS right_table ON left_table.id = right_table.id;"}}}'
fp_fn_join_constant_true_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_join_constant_true_safe")"
[[ "$fp_fn_join_constant_true_safe_output" != *'"code":"join-ineffective-condition"'* ]] || fail "FP/FN corpus excludes restricting JOIN conditions"

fp_fn_not_in_nullable_subquery='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-not-in-nullable-subquery.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnOuter (id INT NOT NULL); CREATE TABLE dbo.FpFnInner (id INT NULL); SELECT o.id FROM dbo.FpFnOuter AS o WHERE o.id NOT IN (SELECT i.id FROM dbo.FpFnInner AS i);"}}}'
fp_fn_not_in_nullable_subquery_output="$(lsp_run "$lsp_initialize" "$fp_fn_not_in_nullable_subquery")"
[[ "$fp_fn_not_in_nullable_subquery_output" == *'"code":"not-in-null"'* && "$fp_fn_not_in_nullable_subquery_output" == *'nullable column'* ]] || fail "FP/FN corpus reports nullable NOT IN subquery sources"

fp_fn_not_in_non_nullable_subquery='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-not-in-non-nullable-subquery.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnOuterSafe (id INT NOT NULL); CREATE TABLE dbo.FpFnInnerSafe (id INT NOT NULL); SELECT o.id FROM dbo.FpFnOuterSafe AS o WHERE o.id NOT IN (SELECT i.id FROM dbo.FpFnInnerSafe AS i);"}}}'
fp_fn_not_in_non_nullable_subquery_output="$(lsp_run "$lsp_initialize" "$fp_fn_not_in_non_nullable_subquery")"
[[ "$fp_fn_not_in_non_nullable_subquery_output" != *'"code":"not-in-null"'* ]] || fail "FP/FN corpus excludes non-nullable NOT IN subquery sources"

fp_fn_insert_non_null_fallback='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-non-null-fallback.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnTarget (id INT NOT NULL); CREATE TABLE dbo.FpFnSource (id INT NULL); INSERT INTO dbo.FpFnTarget (id) SELECT ISNULL(source.id, 0) FROM dbo.FpFnSource AS source; INSERT INTO dbo.FpFnTarget (id) SELECT COALESCE(source.id, 0) FROM dbo.FpFnSource AS source;"}}}'
fp_fn_insert_non_null_fallback_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_non_null_fallback")"
[[ "$fp_fn_insert_non_null_fallback_output" != *'"code":"nullable-source-insert"'* ]] || fail "FP/FN corpus accepts non-null ISNULL and COALESCE fallbacks"

fp_fn_insert_nullable_fallback='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-nullable-fallback.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnTargetNullable (id INT NOT NULL); CREATE TABLE dbo.FpFnSourceNullable (id INT NULL); INSERT INTO dbo.FpFnTargetNullable (id) SELECT COALESCE(source.id, NULL) FROM dbo.FpFnSourceNullable AS source;"}}}'
fp_fn_insert_nullable_fallback_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_nullable_fallback")"
[[ "$fp_fn_insert_nullable_fallback_output" == *'"code":"nullable-source-insert"'* ]] || fail "FP/FN corpus reports nullable COALESCE fallbacks"

fp_fn_insert_case_non_null='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-case-non-null.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnCaseTarget (id INT NOT NULL); CREATE TABLE dbo.FpFnCaseSource (id INT NULL); INSERT INTO dbo.FpFnCaseTarget (id) SELECT CASE WHEN source.id IS NULL THEN ISNULL(source.id, 0) ELSE COALESCE(source.id, 0) END FROM dbo.FpFnCaseSource AS source;"}}}'
fp_fn_insert_case_non_null_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_case_non_null")"
[[ "$fp_fn_insert_case_non_null_output" != *'"code":"nullable-source-insert"'* ]] || fail "FP/FN corpus accepts non-null CASE branches"

fp_fn_insert_case_missing_else='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-case-missing-else.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnCaseTargetNullable (id INT NOT NULL); CREATE TABLE dbo.FpFnCaseSourceNullable (id INT NULL); INSERT INTO dbo.FpFnCaseTargetNullable (id) SELECT CASE WHEN source.id IS NULL THEN source.id END FROM dbo.FpFnCaseSourceNullable AS source;"}}}'
fp_fn_insert_case_missing_else_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_case_missing_else")"
[[ "$fp_fn_insert_case_missing_else_output" == *'"code":"nullable-source-insert"'* ]] || fail "FP/FN corpus reports CASE branches without ELSE"

fp_fn_insert_type_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-type-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnTypeTarget (value NVARCHAR(20)); CREATE TABLE dbo.FpFnTypeSource (value INT); INSERT INTO dbo.FpFnTypeTarget (value) SELECT source.value FROM dbo.FpFnTypeSource AS source;"}}}'
fp_fn_insert_type_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_type_positive")"
[[ "$fp_fn_insert_type_positive_output" == *'"code":"insert-select-type"'* && "$fp_fn_insert_type_positive_output" == *'"severity":3'* ]] || fail "FP/FN corpus reports incompatible INSERT SELECT type families"

fp_fn_insert_type_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-insert-type-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnTypeTargetSafe (value BIGINT); CREATE TABLE dbo.FpFnTypeSourceSafe (value INT); INSERT INTO dbo.FpFnTypeTargetSafe (value) SELECT source.value FROM dbo.FpFnTypeSourceSafe AS source;"}}}'
fp_fn_insert_type_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_insert_type_safe")"
[[ "$fp_fn_insert_type_safe_output" != *'"code":"insert-select-type"'* ]] || fail "FP/FN corpus accepts compatible numeric INSERT SELECT types"

fp_fn_update_type_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-update-type-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnUpdateTarget (value NVARCHAR(20)); CREATE TABLE dbo.FpFnUpdateSource (value INT); UPDATE dbo.FpFnUpdateTarget SET value = source.value FROM dbo.FpFnUpdateSource AS source;"}}}'
fp_fn_update_type_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_update_type_positive")"
[[ "$fp_fn_update_type_positive_output" == *'"code":"update-assignment-type"'* && "$fp_fn_update_type_positive_output" == *'"severity":3'* ]] || fail "FP/FN corpus reports incompatible UPDATE assignment type families"

fp_fn_update_type_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-update-type-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnUpdateTargetSafe (value BIGINT); CREATE TABLE dbo.FpFnUpdateSourceSafe (value INT); UPDATE dbo.FpFnUpdateTargetSafe SET value = source.value FROM dbo.FpFnUpdateSourceSafe AS source;"}}}'
fp_fn_update_type_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_update_type_safe")"
[[ "$fp_fn_update_type_safe_output" != *'"code":"update-assignment-type"'* ]] || fail "FP/FN corpus accepts compatible numeric UPDATE assignment types"

fp_fn_update_nullability_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-update-nullability-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnUpdateNullableTarget (value INT NOT NULL); CREATE TABLE dbo.FpFnUpdateNullableSource (value INT NULL); UPDATE dbo.FpFnUpdateNullableTarget SET value = source.value FROM dbo.FpFnUpdateNullableSource AS source;"}}}'
fp_fn_update_nullability_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_update_nullability_positive")"
[[ "$fp_fn_update_nullability_positive_output" == *'"code":"nullable-source-update"'* ]] || fail "FP/FN corpus reports nullable UPDATE assignments"

fp_fn_update_nullability_safe='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-update-nullability-safe.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnUpdateSafeTarget (value INT NOT NULL); CREATE TABLE dbo.FpFnUpdateSafeSource (value INT NULL); UPDATE dbo.FpFnUpdateSafeTarget SET value = ISNULL(source.value, 0) FROM dbo.FpFnUpdateSafeSource AS source; UPDATE dbo.FpFnUpdateSafeTarget SET value = CASE WHEN source.value IS NULL THEN 0 ELSE 1 END FROM dbo.FpFnUpdateSafeSource AS source;"}}}'
fp_fn_update_nullability_safe_output="$(lsp_run "$lsp_initialize" "$fp_fn_update_nullability_safe")"
[[ "$fp_fn_update_nullability_safe_output" != *'"code":"nullable-source-update"'* ]] || fail "FP/FN corpus accepts non-null UPDATE expressions"

fp_fn_openquery_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-openquery-alias.sql","languageId":"sql","version":1,"text":"SELECT remote_rows.price FROM OPENQUERY([REMOTE], \u0027SELECT price FROM dbo.Prices\u0027) remote_rows;"}}}'
fp_fn_openquery_alias_output="$(lsp_run "$lsp_initialize" "$fp_fn_openquery_alias")"
[[ "$fp_fn_openquery_alias_output" != *'"code":"unbound-identifier"'* && "$fp_fn_openquery_alias_output" != *'"code":"table-name-after-alias"'* ]] || fail "FP/FN corpus recognizes OPENQUERY aliases"

fp_fn_alias_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-alias-scope.sql","languageId":"sql","version":1,"text":"SELECT previous.id FROM dbo.Info previous; SELECT previous.id FROM dbo.Info current_alias;"}}}'
fp_fn_alias_scope_output="$(lsp_run "$lsp_initialize" "$fp_fn_alias_scope")"
fp_fn_alias_scope_count="$(printf '%s' "$fp_fn_alias_scope_output" | grep -o '"code":"unbound-identifier"' | wc -l | tr -d ' ')"
[[ "$fp_fn_alias_scope_count" == "1" ]] || fail "FP/FN corpus keeps table aliases local to one query scope"

fp_fn_correlated_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-correlated-alias.sql","languageId":"sql","version":1,"text":"SELECT outer_rows.id FROM (SELECT id FROM dbo.Info) outer_rows WHERE EXISTS (SELECT 1 WHERE outer_rows.id = 1);"}}}'
fp_fn_correlated_alias_output="$(lsp_run "$lsp_initialize" "$fp_fn_correlated_alias")"
[[ "$fp_fn_correlated_alias_output" != *'"code":"unbound-identifier"'* ]] || fail "FP/FN corpus accepts correlated aliases from an enclosing query"

fp_fn_safe_matrix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-safe-matrix.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnSafe (id INT NOT NULL, title NVARCHAR(50) NULL); CREATE TABLE dbo.FpFnSafeOther (id INT NOT NULL); CREATE INDEX IX_FpFnSafe_Id ON dbo.FpFnSafe (id); CREATE INDEX IX_FpFnSafeOther_Id ON dbo.FpFnSafeOther (id); CREATE PROCEDURE dbo.FpFnSafeProc @id INT, @name NVARCHAR(20) = NULL AS SELECT @id AS id; GO CREATE FUNCTION dbo.FpFnSafeFn(@x INT) RETURNS INT AS BEGIN RETURN @x; END; GO SELECT DISTINCT id, COUNT(*) FROM dbo.FpFnSafe WHERE id = 1 GROUP BY id ORDER BY id, 1; SELECT id FROM dbo.FpFnSafe WHERE id IS NULL AND id NOT IN (1, 2) AND title LIKE N\u0027abc%\u0027 ORDER BY id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY; SELECT dbo.FpFnSafeFn(1); EXEC dbo.FpFnSafeProc @id = 1; UPDATE dbo.FpFnSafe SET title = N\u0027x\u0027 WHERE id = 1; DELETE FROM dbo.FpFnSafe WHERE id = 1; INSERT INTO dbo.FpFnSafe (id, title) VALUES (1, N\u0027x\u0027); SELECT i.id FROM dbo.FpFnSafe i JOIN dbo.FpFnSafeOther o ON i.id = o.id; SELECT TOP (1) id FROM dbo.FpFnSafe; SELECT CASE WHEN id = 1 THEN 1 ELSE 0 END AS state FROM dbo.FpFnSafe;"}}}'
fp_fn_safe_matrix_output="$(lsp_run "$lsp_initialize" "$fp_fn_safe_matrix")"
[[ "$fp_fn_safe_matrix_output" != *'"code":"ambiguous-table"'* && "$fp_fn_safe_matrix_output" != *'"code":"ambiguous-update-target"'* && "$fp_fn_safe_matrix_output" != *'"code":"case-mixed-constants"'* && "$fp_fn_safe_matrix_output" != *'"code":"computed-insert"'* && "$fp_fn_safe_matrix_output" != *'"code":"count-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"cte-alias-shadowing"'* && "$fp_fn_safe_matrix_output" != *'"code":"distinct-order-by"'* && "$fp_fn_safe_matrix_output" != *'"code":"dml-cte-target"'* && "$fp_fn_safe_matrix_output" != *'"code":"duplicate-alias"'* && "$fp_fn_safe_matrix_output" != *'"code":"duplicate-insert-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"duplicate-merge-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"duplicate-procedure-parameter"'* && "$fp_fn_safe_matrix_output" != *'"code":"duplicate-update-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"foreign-key-join-mismatch"'* && "$fp_fn_safe_matrix_output" != *'"code":"function-argument-count"'* && "$fp_fn_safe_matrix_output" != *'"code":"identity-insert"'* && "$fp_fn_safe_matrix_output" != *'"code":"insert-value-count"'* && "$fp_fn_safe_matrix_output" != *'"code":"invalid-alias-reference"'* && "$fp_fn_safe_matrix_output" != *'"code":"invalid-fetch"'* && "$fp_fn_safe_matrix_output" != *'"code":"invalid-procedure-default"'* ]] || fail "FP/FN safe matrix rejects false positives across query and procedure boundaries"
[[ "$fp_fn_safe_matrix_output" != *'"code":"leading-wildcard-like"'* && "$fp_fn_safe_matrix_output" != *'"code":"non-sargable-function"'* && "$fp_fn_safe_matrix_output" != *'"code":"not-in-null"'* && "$fp_fn_safe_matrix_output" != *'"code":"null-comparison"'* && "$fp_fn_safe_matrix_output" != *'"code":"nullable-source-insert"'* && "$fp_fn_safe_matrix_output" != *'"code":"order-by-ordinal"'* && "$fp_fn_safe_matrix_output" != *'"code":"procedure-parameter-type"'* && "$fp_fn_safe_matrix_output" != *'"code":"read-uncommitted"'* && "$fp_fn_safe_matrix_output" != *'"code":"too-many-procedure-arguments"'* && "$fp_fn_safe_matrix_output" != *'"code":"top-zero"'* && "$fp_fn_safe_matrix_output" != *'"code":"ungrouped-select-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"unindexed-join"'* && "$fp_fn_safe_matrix_output" != *'"code":"unindexed-order-by"'* && "$fp_fn_safe_matrix_output" != *'"code":"unindexed-where"'* && "$fp_fn_safe_matrix_output" != *'"code":"unknown-function"'* && "$fp_fn_safe_matrix_output" != *'"code":"unknown-insert-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"unknown-procedure"'* && "$fp_fn_safe_matrix_output" != *'"code":"unknown-procedure-parameter"'* && "$fp_fn_safe_matrix_output" != *'"code":"unknown-update-column"'* && "$fp_fn_safe_matrix_output" != *'"code":"update-without-where"'* ]] || fail "FP/FN safe matrix rejects false positives across DML and access paths"

fp_fn_rule_inventory="$(grep -hEo '(Add|AddLint)\("[a-z0-9-]+' Indexing/DependencyCollector*.cs Analysis/QueryColumnValidator.cs Analysis/ProcedureParameterValidator.cs | sed -E 's/.*\("//' | sort -u)"
while IFS= read -r fp_fn_rule; do
  [[ -z "$fp_fn_rule" ]] && continue
  grep -R -Fq "$fp_fn_rule" tests/smoke || fail "FP/FN corpus has no fixture reference for rule: $fp_fn_rule"
done <<< "$fp_fn_rule_inventory"
