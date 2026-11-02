# ---- FP/FN acceptance corpus ----
fp_fn_constant_dml='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-constant-dml.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET title = \u0027x\u0027 WHERE 1 = 1;"}}}'
fp_fn_constant_dml_output="$(lsp_run "$lsp_initialize" "$fp_fn_constant_dml")"
[[ "$fp_fn_constant_dml_output" == *'"code":"dml-ineffective-filter"'* && "$fp_fn_constant_dml_output" == *'"confidence":"low"'* && "$fp_fn_constant_dml_output" == *'"start":{"line":0,"character":0}'* ]] || fail "FP/FN corpus keeps a deterministic DML true positive precise"

fp_fn_dml_variable='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-dml-variable.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET title = \u0027x\u0027 WHERE id = @id; DELETE FROM dbo.Info WHERE id IN (SELECT id FROM dbo.OtherInfo);"}}}'
fp_fn_dml_variable_output="$(lsp_run "$lsp_initialize" "$fp_fn_dml_variable")"
[[ "$fp_fn_dml_variable_output" != *'"code":"dml-ineffective-filter"'* && "$fp_fn_dml_variable_output" != *'"code":"dml-always-false-filter"'* ]] || fail "FP/FN corpus excludes variable and subquery DML filters"

fp_fn_filter_positive='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-filter-positive.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnNumbers (id INT); SELECT id FROM dbo.FpFnNumbers WHERE id = \u00271\u0027;"}}}'
fp_fn_filter_positive_output="$(lsp_run "$lsp_initialize" "$fp_fn_filter_positive")"
[[ "$fp_fn_filter_positive_output" == *'"code":"implicit-filter-conversion"'* && "$fp_fn_filter_positive_output" == *'"confidence":"low"'* ]] || fail "FP/FN corpus keeps a filter conversion true positive low confidence"

fp_fn_filter_negative='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-filter-negative.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnNumbers (id INT); SELECT id FROM dbo.FpFnNumbers WHERE id = 1;"}}}'
fp_fn_filter_negative_output="$(lsp_run "$lsp_initialize" "$fp_fn_filter_negative")"
[[ "$fp_fn_filter_negative_output" != *'"code":"implicit-filter-conversion"'* ]] || fail "FP/FN corpus excludes compatible numeric filters"

fp_fn_result_types='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-result-types.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnBig (value BIGINT); CREATE PROCEDURE dbo.FpFnValue AS SELECT CAST(1 AS INT) AS value; INSERT INTO dbo.FpFnBig (value) EXEC dbo.FpFnValue; CREATE TABLE dbo.FpFnText (value NVARCHAR(20)); CREATE PROCEDURE dbo.FpFnTextValue AS SELECT CAST(\u0027x\u0027 AS VARCHAR(20)) AS value; INSERT INTO dbo.FpFnText (value) EXEC dbo.FpFnTextValue;"}}}'
fp_fn_result_types_output="$(lsp_run "$lsp_initialize" "$fp_fn_result_types")"
[[ "$fp_fn_result_types_output" != *'"code":"insert-exec-type"'* ]] || fail "FP/FN corpus excludes compatible INSERT EXEC type families"

fp_fn_nullability='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-nullability.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.FpFnDefault (id INT NOT NULL DEFAULT 1); INSERT INTO dbo.FpFnDefault DEFAULT VALUES; CREATE TABLE dbo.FpFnRequired (id INT NOT NULL); INSERT INTO dbo.FpFnRequired (id) VALUES (NULL);"}}}'
fp_fn_nullability_output="$(lsp_run "$lsp_initialize" "$fp_fn_nullability")"
[[ "$fp_fn_nullability_output" == *'"code":"nullability-violation"'* && "$fp_fn_nullability_output" != *'"code":"missing-required-insert-column"'* ]] || fail "FP/FN corpus distinguishes NULL violations from defaulted INSERTs"

fp_fn_lint_edges='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-fp-fn-lint-edges.sql","languageId":"sql","version":1,"text":"BEGIN TRANSACTION; SELECT * FROM dbo.Info WITH (NOLOCK); SELECT * FROM dbo.Info WITH (READUNCOMMITTED); COMMIT; DELETE FROM dbo.Info;"}}}'
fp_fn_lint_edges_output="$(lsp_run "$lsp_initialize" "$fp_fn_lint_edges")"
[[ "$fp_fn_lint_edges_output" == *'"code":"nolock"'* && "$fp_fn_lint_edges_output" == *'"code":"nolock-in-transaction"'* && "$fp_fn_lint_edges_output" == *'"code":"read-uncommitted"'* && "$fp_fn_lint_edges_output" == *'"code":"delete-without-where"'* ]] || fail "FP/FN corpus covers transaction and DELETE safety diagnostics"

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
