lsp_signature_help='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-signature-help.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id INT, @name NVARCHAR(20) = NULL AS SELECT @id;\nGO\nEXEC dbo.DoWork 1, "}}}'
lsp_signature_help_request='{"jsonrpc":"2.0","id":17,"method":"textDocument/signatureHelp","params":{"textDocument":{"uri":"file:///tmp/sqldeps-signature-help.sql"},"position":{"line":2,"character":19}}}'
lsp_signature_help_output="$(lsp_run "$lsp_initialize" "$lsp_signature_help" "$lsp_signature_help_request")"
[[ "$lsp_signature_help_output" == *'"id":17'* && "$lsp_signature_help_output" == *'"label":"@id INT"'* && "$lsp_signature_help_output" == *'"label":"@name NVARCHAR (20) = NULL"'* && "$lsp_signature_help_output" == *'"activeParameter":1'* ]] || fail "LSP serves procedure signature help"
lsp_semantic_tokens='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-semantic-tokens.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.TokenTable (id INT);\nSELECT * FROM dbo.TokenTable;\nSELECT * FROM dbo.UnknownTable;"}}}'
lsp_semantic_tokens_request='{"jsonrpc":"2.0","id":19,"method":"textDocument/semanticTokens/full","params":{"textDocument":{"uri":"file:///tmp/sqldeps-semantic-tokens.sql"}}}'
lsp_semantic_tokens_output="$(lsp_run "$lsp_initialize" "$lsp_semantic_tokens" "$lsp_semantic_tokens_request")"
[[ "$lsp_semantic_tokens_output" == *'"semanticTokensProvider"'* && "$lsp_semantic_tokens_output" == *'"id":19,"result":{"data":[0,13,14,0,0,1,14,14,0,0]}'* ]] || fail "LSP returns semantic tokens only for resolved objects"

lsp_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql","languageId":"sql","version":1,"text":"SELECT v. FROM dbo.Info v;"}}}'
lsp_completion='{"jsonrpc":"2.0","id":4,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql"},"position":{"line":0,"character":9}}}'
lsp_completion_output="$(lsp_run "$lsp_initialize" "$lsp_schema" "$lsp_query" "$lsp_completion")"
[[ "$lsp_completion_output" == *'"label":"id"'* && "$lsp_completion_output" == *'"label":"title"'* ]] || fail "LSP completes columns through a table alias"
lsp_table_variable_completion='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-variable-completion.sql","languageId":"sql","version":1,"text":"DECLARE @Rows TABLE (col_a INT, col_b INT); SELECT fl.col FROM @Rows fl;"}}}'
lsp_table_variable_completion_request='{"jsonrpc":"2.0","id":13,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-variable-completion.sql"},"position":{"line":0,"character":57}}}'
lsp_table_variable_completion_output="$(lsp_run "$lsp_initialize" "$lsp_table_variable_completion" "$lsp_table_variable_completion_request")"
[[ "$lsp_table_variable_completion_output" == *'"label":"col_a"'* && "$lsp_table_variable_completion_output" == *'"label":"col_b"'* ]] || fail "LSP completes table variable columns through an alias"

lsp_procedure_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-procedure-scope.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.FirstProc AS DECLARE @first TABLE (first_id INT); SELECT f.first_id FROM @first f;\nGO\nCREATE PROCEDURE dbo.SecondProc AS SELECT s. FROM @first s;"}}}'
lsp_procedure_scope_request='{"jsonrpc":"2.0","id":15,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-procedure-scope.sql"},"position":{"line":2,"character":44}}}'
lsp_procedure_scope_output="$(lsp_run "$lsp_initialize" "$lsp_procedure_scope" "$lsp_procedure_scope_request")"
[[ "$lsp_procedure_scope_output" != *'"label":"first_id"'* ]] || fail "LSP isolates local columns between procedures"
lsp_dropped_completion='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dropped-completion.sql","languageId":"sql","version":1,"text":"CREATE TABLE #dropped (id INT);\nDROP TABLE #dropped;\nSELECT d."}}}'
lsp_dropped_completion_request='{"jsonrpc":"2.0","id":14,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dropped-completion.sql"},"position":{"line":2,"character":9}}}'
lsp_dropped_completion_output="$(lsp_run "$lsp_initialize" "$lsp_dropped_completion" "$lsp_dropped_completion_request")"
[[ "$lsp_dropped_completion_output" != *'"label":"id"'* ]] || fail "LSP does not complete dropped temporary-table columns"
lsp_multiline_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-multiline-query.sql","languageId":"sql","version":1,"text":"SELECT v.\nFROM dbo.Info v;"}}}'
lsp_multiline_completion='{"jsonrpc":"2.0","id":10,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-multiline-query.sql"},"position":{"line":0,"character":9}}}'
lsp_multiline_completion_output="$(lsp_run "$lsp_initialize" "$lsp_schema" "$lsp_multiline_query" "$lsp_multiline_completion")"
[[ "$lsp_multiline_completion_output" == *'"label":"id"'* && "$lsp_multiline_completion_output" == *'"label":"title"'* ]] || fail "LSP completes aliases declared below a select list"
lsp_cte_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql","languageId":"sql","version":1,"text":"WITH active (id, title) AS (SELECT id, title FROM dbo.Info)\nSELECT active.id FROM active;"}}}'
lsp_cte_completion='{"jsonrpc":"2.0","id":7,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql"},"position":{"line":1,"character":14}}}'
lsp_cte_completion_output="$(lsp_run "$lsp_initialize" "$lsp_cte_query" "$lsp_cte_completion")"
[[ "$lsp_cte_completion_output" == *'"label":"id"'* && "$lsp_cte_completion_output" == *'"label":"title"'* && "$lsp_cte_completion_output" == *'"detail":"CTE"'* ]] || fail "LSP completes explicit CTE columns"
lsp_invalid_cte_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-cte-column.sql","languageId":"sql","version":1,"text":"WITH active (id, title) AS (SELECT 1, N\u0027title\u0027) SELECT active.missing FROM active;"}}}'
lsp_invalid_cte_column_output="$(lsp_run "$lsp_initialize" "$lsp_invalid_cte_column")"
[[ "$lsp_invalid_cte_column_output" == *'"code":"unknown-column"'* && "$lsp_invalid_cte_column_output" == *'Invalid column name'* ]] || fail "LSP validates CTE columns"
lsp_derived_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql","languageId":"sql","version":1,"text":"SELECT q.id FROM (SELECT id, title FROM dbo.Info) q;"}}}'
lsp_derived_completion='{"jsonrpc":"2.0","id":8,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql"},"position":{"line":0,"character":9}}}'
lsp_derived_completion_output="$(lsp_run "$lsp_initialize" "$lsp_derived_query" "$lsp_derived_completion")"
[[ "$lsp_derived_completion_output" == *'"label":"id"'* && "$lsp_derived_completion_output" == *'"label":"title"'* && "$lsp_derived_completion_output" == *'"detail":"derived column"'* ]] || fail "LSP completes derived table columns"
lsp_derived_join='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-join.sql","languageId":"sql","version":1,"text":"CREATE TABLE #left_side (left_id INT); CREATE TABLE #right_side (right_id INT); SELECT q.right_id FROM (SELECT * FROM #left_side JOIN #right_side ON 1 = 1) q;"}}}'
lsp_derived_join_output="$(lsp_run "$lsp_initialize" "$lsp_derived_join")"
[[ "$lsp_derived_join_output" != *'"code":"unknown-column"'* ]] || fail "LSP carries all local join columns into derived queries"
lsp_invalid_derived_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-derived-column.sql","languageId":"sql","version":1,"text":"SELECT q.missing FROM (SELECT 1 AS id) q;"}}}'
lsp_invalid_derived_column_output="$(lsp_run "$lsp_initialize" "$lsp_invalid_derived_column")"
[[ "$lsp_invalid_derived_column_output" == *'"code":"unknown-column"'* && "$lsp_invalid_derived_column_output" == *'Invalid column name'* ]] || fail "LSP validates derived table columns"
lsp_object_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql","languageId":"sql","version":1,"text":"EXEC dbo."}}}'
lsp_object_completion='{"jsonrpc":"2.0","id":5,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql"},"position":{"line":0,"character":9}}}'
lsp_object_completion_output="$(lsp_run "$lsp_initialize" "$lsp_open" "$lsp_object_query" "$lsp_object_completion")"
[[ "$lsp_object_completion_output" == *'"label":"dbo.demo"'* ]] || fail "LSP completes workspace object names"
