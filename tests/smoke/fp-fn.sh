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
