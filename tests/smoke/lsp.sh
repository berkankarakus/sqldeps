lsp_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.demo AS SELECT 1;\nEXEC dbo.demo;"}}}'
lsp_definition='{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10}}}'
lsp_references='{"jsonrpc":"2.0","id":3,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10},"context":{"includeDeclaration":true}}}'
lsp_output="$(lsp_run "$lsp_initialize" "$lsp_open" "$lsp_definition" "$lsp_references")"
[[ "$lsp_output" == *'"documentSymbolProvider":true'* ]] || fail "LSP advertises document symbols"
[[ "$lsp_output" == *'"change":2'* ]] || fail "LSP advertises incremental document synchronization"
[[ "$lsp_output" == *'"uri":"file:///tmp/sqldeps-lsp-test.sql"'* ]] || fail "LSP resolves definitions in an open document"
[[ "$lsp_output" == *'"positionEncoding":"utf-16"'* && "$lsp_output" == *'"id":3,"result":['* ]] || fail "LSP returns compatible reference locations"
lsp_column_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-column-navigation.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT, title VARCHAR(10));\nSELECT i.id FROM dbo.Info AS i;"}}}'
lsp_column_definition='{"jsonrpc":"2.0","id":7,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/sqldeps-column-navigation.sql"},"position":{"line":1,"character":10}}}'
lsp_column_references='{"jsonrpc":"2.0","id":8,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/sqldeps-column-navigation.sql"},"position":{"line":1,"character":10},"context":{"includeDeclaration":true}}}'
lsp_column_output="$(lsp_run "$lsp_initialize" "$lsp_column_open" "$lsp_column_definition" "$lsp_column_references")"
[[ "$lsp_column_output" == *'"id":7,"result":[{"uri":"file:///tmp/sqldeps-column-navigation.sql"'* ]] || fail "LSP resolves aliased column definitions"
[[ "$lsp_column_output" == *'"id":8,"result"'* && "$lsp_column_output" == *'"line":0'* && "$lsp_column_output" == *'"line":1'* ]] || fail "LSP returns column references with the declaration"
lsp_call_hierarchy_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-call-hierarchy.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.caller AS EXEC dbo.demo;\nGO\nCREATE PROCEDURE dbo.demo AS SELECT 1;"}}}'
lsp_call_hierarchy_prepare='{"jsonrpc":"2.0","id":4,"method":"textDocument/prepareCallHierarchy","params":{"textDocument":{"uri":"file:///tmp/sqldeps-call-hierarchy.sql"},"position":{"line":0,"character":40}}}'
lsp_call_hierarchy_incoming='{"jsonrpc":"2.0","id":5,"method":"callHierarchy/incomingCalls","params":{"item":{"name":"dbo.demo","data":{"name":"dbo.demo"}}}}'
lsp_call_hierarchy_outgoing='{"jsonrpc":"2.0","id":6,"method":"callHierarchy/outgoingCalls","params":{"item":{"name":"dbo.caller","data":{"name":"dbo.caller"}}}}'
lsp_call_hierarchy_output="$(lsp_run "$lsp_initialize" "$lsp_call_hierarchy_open" "$lsp_call_hierarchy_prepare" "$lsp_call_hierarchy_incoming" "$lsp_call_hierarchy_outgoing")"
[[ "$lsp_call_hierarchy_output" == *'"callHierarchyProvider":true'* && "$lsp_call_hierarchy_output" == *'"id":4,"result":[{"name":"dbo.demo"'* ]] || fail "LSP prepares indexed call hierarchy items"
[[ "$lsp_call_hierarchy_output" == *'"id":5,"result":[{"from":{"name":"dbo.caller"'* ]] || fail "LSP returns incoming procedure calls"
[[ "$lsp_call_hierarchy_output" == *'"id":6,"result":[{"to":{"name":"dbo.demo"'* ]] || fail "LSP returns outgoing procedure calls"
lsp_format_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-format.sql","languageId":"sql","version":1,"text":"select * from dbo.Info where id=1;"}}}'
lsp_format_request='{"jsonrpc":"2.0","id":10,"method":"textDocument/formatting","params":{"textDocument":{"uri":"file:///tmp/sqldeps-format.sql"},"options":{"tabSize":4,"insertSpaces":true}}}'
lsp_format_output="$(lsp_run "$lsp_initialize" "$lsp_format_document" "$lsp_format_request")"
[[ "$lsp_format_output" == *'"documentFormattingProvider":true'* && "$lsp_format_output" == *'"id":10,"result":[{"range":{"start":{"line":0,"character":0},'* && "$lsp_format_output" == *'"newText":"SELECT'* ]] || fail "LSP returns a safe SQL formatting preview"
lsp_parse_prefix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-prefix.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.PrefixTable (id INT);\nSELECT FROM;"}}}'
lsp_parse_prefix_symbols='{"jsonrpc":"2.0","id":4,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-prefix.sql"}}}'
lsp_parse_prefix_output="$(lsp_run "$lsp_initialize" "$lsp_parse_prefix" "$lsp_parse_prefix_symbols")"
[[ "$lsp_parse_prefix_output" == *'Incorrect syntax'* && "$lsp_parse_prefix_output" == *'"name":"dbo.PrefixTable"'* ]] || fail "LSP preserves valid symbols before a parse error"
lsp_change_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.BeforeChange (id INT);"}}}'
lsp_change='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql","version":2},"contentChanges":[{"text":"CREATE TABLE dbo.AfterChange (id INT);"}]}}'
lsp_change_symbols='{"jsonrpc":"2.0","id":5,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql"}}}'
lsp_change_output="$(lsp_run "$lsp_initialize" "$lsp_change_open" "$lsp_change" "$lsp_change_symbols")"
[[ "$lsp_change_output" == *'"name":"dbo.AfterChange"'* && "$lsp_change_output" != *'"name":"dbo.BeforeChange"'* ]] || fail "LSP replaces stale symbols after a rapid document change"
lsp_incremental_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-incremental.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.BeforeChange (id INT);"}}}'
lsp_incremental_change='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-incremental.sql","version":2},"contentChanges":[{"range":{"start":{"line":0,"character":17},"end":{"line":0,"character":29}},"rangeLength":12,"text":"AfterChange"}]}}'
lsp_incremental_symbols='{"jsonrpc":"2.0","id":6,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-incremental.sql"}}}'
lsp_incremental_output="$(lsp_run "$lsp_initialize" "$lsp_incremental_open" "$lsp_incremental_change" "$lsp_incremental_symbols")"
[[ "$lsp_incremental_output" == *'"name":"dbo.AfterChange"'* && "$lsp_incremental_output" != *'"name":"dbo.BeforeChange"'* ]] || fail "LSP applies range-based document changes"
lsp_out_of_order_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-out-of-order.sql","languageId":"sql","version":2,"text":"CREATE TABLE dbo.LatestChange (id INT);"}}}'
lsp_out_of_order_new='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-out-of-order.sql","version":3},"contentChanges":[{"text":"CREATE TABLE dbo.NewestChange (id INT);"}]}}'
lsp_out_of_order_old='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-out-of-order.sql","version":2},"contentChanges":[{"text":"CREATE TABLE dbo.OldChange (id INT);"}]}}'
lsp_out_of_order_symbols='{"jsonrpc":"2.0","id":41,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-out-of-order.sql"}}}'
lsp_out_of_order_output="$(lsp_run "$lsp_initialize" "$lsp_out_of_order_open" "$lsp_out_of_order_new" "$lsp_out_of_order_old" "$lsp_out_of_order_symbols")"
[[ "$lsp_out_of_order_output" == *'"name":"dbo.NewestChange"'* && "$lsp_out_of_order_output" != *'"name":"dbo.OldChange"'* ]] || fail "LSP ignores out-of-order document changes"
lsp_non_file_uri='{"jsonrpc":"2.0","id":42,"method":"textDocument/didOpen","params":{"textDocument":{"uri":"untitled:outside.sql","languageId":"sql","version":1,"text":"SELECT 1;"}}}'
lsp_non_file_uri_output="$(lsp_run "$lsp_initialize" "$lsp_non_file_uri")"
[[ "$lsp_non_file_uri_output" == *'"id":42,"error":{"code":-32602'* && "$lsp_non_file_uri_output" != *'"id":42,"result"'* ]] || fail "LSP rejects non-file document URIs"
lsp_unknown_procedure='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-procedure.sql","languageId":"sql","version":1,"text":"EXEC dbo.Missing @id = 1;"}}}'
lsp_unknown_procedure_output="$(lsp_run "$lsp_initialize" "$lsp_unknown_procedure")"
[[ "$lsp_unknown_procedure_output" == *'"code":"unknown-procedure"'* && "$lsp_unknown_procedure_output" == *'was not found in the workspace index'* && "$lsp_unknown_procedure_output" == *'"severity":2'* && "$lsp_unknown_procedure_output" == *'"confidence":"medium"'* ]] || fail "LSP reports unknown qualified procedures"
lsp_unknown_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-function.sql","languageId":"sql","version":1,"text":"SELECT dbo.MissingFn(1);"}}}'
lsp_unknown_function_output="$(lsp_run "$lsp_initialize" "$lsp_unknown_function")"
[[ "$lsp_unknown_function_output" == *'"code":"unknown-function"'* && "$lsp_unknown_function_output" == *'was not found in the workspace index'* && "$lsp_unknown_function_output" == *'"severity":2'* && "$lsp_unknown_function_output" == *'"confidence":"medium"'* ]] || fail "LSP reports unknown qualified functions"
lsp_unknown_unqualified_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-unqualified-function.sql","languageId":"sql","version":1,"text":"SELECT MissingFn(1);"}}}'
lsp_unknown_unqualified_function_output="$(lsp_run "$lsp_initialize" "$lsp_unknown_unqualified_function")"
[[ "$lsp_unknown_unqualified_function_output" == *'"code":"unknown-function"'* && "$lsp_unknown_unqualified_function_output" == *'"severity":3'* && "$lsp_unknown_unqualified_function_output" == *'"confidence":"low"'* ]] || fail "LSP reports unqualified unknown functions as low-confidence information"
lsp_external_system_procedure='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-external-system-procedure.sql","languageId":"sql","version":1,"text":"EXEC msdb.dbo.sp_send_dbmail @profile_name = \u0027alerts\u0027;"}}}'
lsp_external_system_procedure_output="$(lsp_run "$lsp_initialize" "$lsp_external_system_procedure")"
[[ "$lsp_external_system_procedure_output" != *'"code":"unknown-procedure"'* ]] || fail "LSP accepts qualified SQL Server system procedures outside the workspace"
lsp_builtin_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-builtin-function.sql","languageId":"sql","version":1,"text":"SELECT SUM(id), COUNT(id), ISNULL(id, 0), dbo.OBJECT_ID(N\u0027dbo.Info\u0027), dbo.ISNUMERIC(id) FROM dbo.Info;"}}}'
lsp_builtin_function_output="$(lsp_run "$lsp_initialize" "$lsp_builtin_function")"
[[ "$lsp_builtin_function_output" != *'"code":"unknown-function"'* ]] || fail "LSP does not treat SQL Server built-in functions as workspace functions"
lsp_unknown_unqualified_procedure='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-unqualified-procedure.sql","languageId":"sql","version":1,"text":"EXEC MissingProc;"}}}'
lsp_unknown_unqualified_procedure_output="$(lsp_run "$lsp_initialize" "$lsp_unknown_unqualified_procedure")"
[[ "$lsp_unknown_unqualified_procedure_output" == *'"code":"unknown-procedure"'* && "$lsp_unknown_unqualified_procedure_output" == *'"severity":3'* && "$lsp_unknown_unqualified_procedure_output" == *'"confidence":"low"'* ]] || fail "LSP reports unqualified unknown procedures as low-confidence information"
lsp_excess_procedure_argument='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-excess-procedure-argument.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id int AS SELECT @id;\nGO\nEXEC dbo.DoWork 1, 2;"}}}'
lsp_excess_procedure_argument_output="$(lsp_run "$lsp_initialize" "$lsp_excess_procedure_argument")"
[[ "$lsp_excess_procedure_argument_output" == *'"code":"too-many-procedure-arguments"'* && "$lsp_excess_procedure_argument_output" == *'accepts at most 1 argument'* ]] || fail "LSP validates procedure argument count from the index"
lsp_missing_procedure_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-missing-procedure-parameter.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id int, @name nvarchar(20), @mode int = 1 AS SELECT @id;\nGO\nEXEC dbo.DoWork @id = 1;"}}}'
lsp_missing_procedure_parameter_output="$(lsp_run "$lsp_initialize" "$lsp_missing_procedure_parameter")"
[[ "$lsp_missing_procedure_parameter_output" == *'"code":"missing-procedure-parameter"'* && "$lsp_missing_procedure_parameter_output" == *'requires parameter'* ]] || fail "LSP validates required procedure parameters from the index"
lsp_default_nullable_procedure_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-default-nullable-procedure-parameter.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.OptionalWork @id int, @name nvarchar(20) = NULL, @mode int = 1 AS SELECT @id;\nGO\nEXEC dbo.OptionalWork @id = 1;"}}}'
lsp_default_nullable_procedure_parameter_output="$(lsp_run "$lsp_initialize" "$lsp_default_nullable_procedure_parameter")"
[[ "$lsp_default_nullable_procedure_parameter_output" != *'"code":"missing-procedure-parameter"'* ]] || fail "LSP accepts omitted default and nullable procedure parameters"
lsp_invalid_procedure_default='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-procedure-default.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.RequiredWork @id int AS SELECT @id;\nGO\nEXEC dbo.RequiredWork DEFAULT;"}}}'
lsp_invalid_procedure_default_output="$(lsp_run "$lsp_initialize" "$lsp_invalid_procedure_default")"
[[ "$lsp_invalid_procedure_default_output" == *'"code":"invalid-procedure-default"'* && "$lsp_invalid_procedure_default_output" == *'has no default value'* ]] || fail "LSP rejects DEFAULT for required procedure parameters"
lsp_ignored_output_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ignored-output-parameter.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.GetValue @value INT OUTPUT AS SELECT @value;\nGO\nDECLARE @result INT; EXEC dbo.GetValue @value = @result;"}}}'
lsp_ignored_output_parameter_output="$(lsp_run "$lsp_initialize" "$lsp_ignored_output_parameter")"
[[ "$lsp_ignored_output_parameter_output" == *'"code":"procedure-output-ignored"'* && "$lsp_ignored_output_parameter_output" == *'not marked OUTPUT'* ]] || fail "LSP reports ignored procedure output parameters"
lsp_invalid_output_argument='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-output-argument.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.GetValue @value INT OUTPUT AS SELECT @value;\nGO\nEXEC dbo.GetValue @value = 1 OUTPUT;"}}}'
lsp_invalid_output_argument_output="$(lsp_run "$lsp_initialize" "$lsp_invalid_output_argument")"
[[ "$lsp_invalid_output_argument_output" == *'Cannot use the OUTPUT option when passing a constant to a stored procedure'* ]] || fail "LSP reports literal OUTPUT procedure arguments"
lsp_mixed_procedure_arguments='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-mixed-procedure-arguments.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.Mixed @id INT, @name NVARCHAR(20), @mode INT = 1, @out INT OUTPUT AS SELECT @id;\nGO\nDECLARE @value INT; EXEC dbo.Mixed 1, @name = N\u0027x\u0027, @out = @value OUTPUT;"}}}'
lsp_mixed_procedure_arguments_output="$(lsp_run "$lsp_initialize" "$lsp_mixed_procedure_arguments")"
[[ "$lsp_mixed_procedure_arguments_output" != *'"code":"missing-procedure-parameter"'* && "$lsp_mixed_procedure_arguments_output" != *'"code":"procedure-output-ignored"'* ]] || fail "LSP binds mixed positional and named procedure arguments"
lsp_procedure_parameter_type='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-procedure-parameter-type.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.Typed @id INT, @key UNIQUEIDENTIFIER AS SELECT @id;\nGO\nEXEC dbo.Typed @id = \u0027not-a-number\u0027, @key = \u0027not-a-guid\u0027;"}}}'
lsp_procedure_parameter_type_output="$(lsp_run "$lsp_initialize" "$lsp_procedure_parameter_type")"
[[ "$lsp_procedure_parameter_type_output" == *'"code":"procedure-parameter-type"'* && "$lsp_procedure_parameter_type_output" == *'cannot be converted'* ]] || fail "LSP reports definite procedure parameter literal type mismatches"
lsp_function_argument_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-function-argument-count.sql","languageId":"sql","version":1,"text":"CREATE FUNCTION dbo.AddOne(@value int) RETURNS int AS BEGIN RETURN @value + 1; END\nGO\nSELECT dbo.AddOne(1, 2);"}}}'
lsp_function_argument_count_output="$(lsp_run "$lsp_initialize" "$lsp_function_argument_count")"
[[ "$lsp_function_argument_count_output" == *'"code":"function-argument-count"'* && "$lsp_function_argument_count_output" == *'expects 1 argument'* ]] || fail "LSP validates function argument count from the index"
lsp_case_constants='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-case-constants.sql","languageId":"sql","version":1,"text":"SELECT CASE WHEN id = 1 THEN 1 ELSE N\u0027unknown\u0027 END FROM dbo.Info;"}}}'
lsp_case_constants_output="$(lsp_run "$lsp_initialize" "$lsp_case_constants")"
[[ "$lsp_case_constants_output" == *'"code":"case-mixed-constants"'* && "$lsp_case_constants_output" == *'mixed constant types'* ]] || fail "LSP explains mixed CASE constant types"
lsp_distinct_order_by='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-distinct-order-by.sql","languageId":"sql","version":1,"text":"SELECT DISTINCT id FROM dbo.Info ORDER BY name;"}}}'
lsp_distinct_order_by_output="$(lsp_run "$lsp_initialize" "$lsp_distinct_order_by")"
[[ "$lsp_distinct_order_by_output" == *'"code":"distinct-order-by"'* && "$lsp_distinct_order_by_output" == *'must appear in the SELECT list'* ]] || fail "LSP validates DISTINCT ORDER BY columns"
lsp_leading_wildcard='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-leading-wildcard.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE title LIKE N\u0027%term\u0027;"}}}'
lsp_leading_wildcard_output="$(lsp_run "$lsp_initialize" "$lsp_leading_wildcard")"
[[ "$lsp_leading_wildcard_output" == *'"code":"leading-wildcard-like"'* && "$lsp_leading_wildcard_output" == *'prevents an index seek'* ]] || fail "LSP reports leading wildcard LIKE patterns"
lsp_non_sargable_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-non-sargable-function.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Products (name NVARCHAR(50)); SELECT * FROM dbo.Products WHERE LOWER(name) = N\u0027x\u0027;"}}}'
lsp_non_sargable_function_output="$(lsp_run "$lsp_initialize" "$lsp_non_sargable_function")"
[[ "$lsp_non_sargable_function_output" == *'"code":"non-sargable-function"'* && "$lsp_non_sargable_function_output" == *'prevent an index seek'* ]] || fail "LSP reports function-wrapped predicates"
lsp_not_in_subquery='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-not-in-subquery.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id NOT IN (SELECT id FROM dbo.OtherInfo);"}}}'
lsp_not_in_subquery_output="$(lsp_run "$lsp_initialize" "$lsp_not_in_subquery")"
[[ "$lsp_not_in_subquery_output" != *'"code":"not-in-subquery"'* ]] || fail "LSP avoids speculative nullable NOT IN warnings"
lsp_cte_alias_shadowing='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-alias-shadowing.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Products (id INT); WITH active AS (SELECT 1 AS id) SELECT p.id FROM dbo.Products active JOIN active p ON p.id = active.id;"}}}'
lsp_cte_alias_shadowing_output="$(lsp_run "$lsp_initialize" "$lsp_cte_alias_shadowing")"
[[ "$lsp_cte_alias_shadowing_output" == *'"code":"cte-alias-shadowing"'* && "$lsp_cte_alias_shadowing_output" == *'shadows a CTE'* ]] || fail "LSP reports CTE alias shadowing"
lsp_set_operation_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-set-operation-count.sql","languageId":"sql","version":1,"text":"SELECT 1 UNION SELECT 2, 3;"}}}'
lsp_set_operation_count_output="$(lsp_run "$lsp_initialize" "$lsp_set_operation_count")"
[[ "$lsp_set_operation_count_output" == *'"code":"set-operation-column-count"'* && "$lsp_set_operation_count_output" == *'same number of columns'* ]] || fail "LSP validates set operation column counts"
lsp_ungrouped_select='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ungrouped-select.sql","languageId":"sql","version":1,"text":"SELECT id, title, COUNT(*) FROM dbo.Info GROUP BY id;"}}}'
lsp_ungrouped_select_output="$(lsp_run "$lsp_initialize" "$lsp_ungrouped_select")"
[[ "$lsp_ungrouped_select_output" == *'"code":"ungrouped-select-column"'* && "$lsp_ungrouped_select_output" == *'included in GROUP BY'* ]] || fail "LSP reports ungrouped selected columns"
lsp_ungrouped_expression='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ungrouped-expression.sql","languageId":"sql","version":1,"text":"SELECT id + 1, COUNT(*) FROM dbo.Info GROUP BY title;"}}}'
lsp_ungrouped_expression_output="$(lsp_run "$lsp_initialize" "$lsp_ungrouped_expression")"
[[ "$lsp_ungrouped_expression_output" == *'"code":"ungrouped-select-column"'* && "$lsp_ungrouped_expression_output" == *'Column'* ]] || fail "LSP reports columns nested in ungrouped expressions"
lsp_prefix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.good AS SELECT 1;\nTHIS IS NOT VALID SQL"}}}'
lsp_prefix_symbols='{"jsonrpc":"2.0","id":6,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql"}}}'
lsp_prefix_output="$(lsp_run "$lsp_initialize" "$lsp_prefix" "$lsp_prefix_symbols")"
[[ "$lsp_prefix_output" == *'"name":"dbo.good"'* && "$lsp_prefix_output" == *'Incorrect syntax near'* ]] || fail "LSP retains symbols before a parse error"
lsp_parse_error_batch='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-error-batch.sql","languageId":"sql","version":1,"text":"THIS IS NOT VALID SQL\nGO\nCREATE PROCEDURE dbo.after_error AS SELECT 1;"}}}'
lsp_parse_error_batch_symbols='{"jsonrpc":"2.0","id":7,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-error-batch.sql"}}}'
lsp_parse_error_batch_output="$(lsp_run "$lsp_initialize" "$lsp_parse_error_batch" "$lsp_parse_error_batch_symbols")"
[[ "$lsp_parse_error_batch_output" == *'"name":"dbo.after_error"'* && "$lsp_parse_error_batch_output" == *'Incorrect syntax near'* ]] || fail "LSP indexes batches after a parse error"
lsp_syntax='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-syntax.sql","languageId":"sql","version":1,"text":"SELECT FROM dbo.Info;"}}}'
lsp_syntax_output="$(lsp_run "$lsp_initialize" "$lsp_syntax")"
[[ "$lsp_syntax_output" == *'Incorrect syntax near'* && "$lsp_syntax_output" == *'"start":{"line":0,"character":7}'* ]] || fail "LSP marks the parser error at its exact column"
lsp_alias_reference='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-alias-reference.sql","languageId":"sql","version":1,"text":"SELECT i. i.col_d FROM dbo.Info i;"}}}'
lsp_alias_reference_output="$(lsp_run "$lsp_initialize" "$lsp_alias_reference")"
[[ "$lsp_alias_reference_output" == *'"code":"invalid-alias-reference"'* && "$lsp_alias_reference_output" == *'A comma may be missing'* ]] || fail "LSP explains a likely missing comma in an alias reference"
lsp_cross_alias_reference='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cross-alias-reference.sql","languageId":"sql","version":1,"text":"SELECT p. i.col_d FROM dbo.Product p JOIN dbo.Info i ON 1 = 1;"}}}'
lsp_cross_alias_reference_output="$(lsp_run "$lsp_initialize" "$lsp_cross_alias_reference")"
[[ "$lsp_cross_alias_reference_output" == *'"code":"invalid-alias-reference"'* && "$lsp_cross_alias_reference_output" == *'A comma may be missing'* ]] || fail "LSP recognizes a missing comma before another table alias"
lsp_unbound_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unbound-alias.sql","languageId":"sql","version":1,"text":"SELECT x.col_d FROM dbo.Info i;"}}}'
lsp_unbound_alias_output="$(lsp_run "$lsp_initialize" "$lsp_unbound_alias")"
[[ "$lsp_unbound_alias_output" == *'"code":"unbound-identifier"'* && "$lsp_unbound_alias_output" == *'could not be bound'* ]] || fail "LSP reports an unknown table alias"
lsp_derived_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-alias.sql","languageId":"sql","version":1,"text":"SELECT n.col_b FROM (SELECT p.col_b FROM dbo.tbl_b p) n JOIN dbo.tbl_c d ON n.col_b = d.col_b;"}}}'
lsp_derived_alias_output="$(lsp_run "$lsp_initialize" "$lsp_derived_alias")"
[[ "$lsp_derived_alias_output" != *'"code":"unbound-identifier"'* ]] || fail "LSP accepts aliases from derived tables"
lsp_derived_column_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-column-alias.sql","languageId":"sql","version":1,"text":"SELECT n.code FROM (SELECT p.id FROM dbo.Info p) n(code);"}}}'
lsp_derived_column_alias_output="$(lsp_run "$lsp_initialize" "$lsp_derived_column_alias")"
[[ "$lsp_derived_column_alias_output" != *'"code":"unbound-identifier"'* && "$lsp_derived_column_alias_output" != *'"code":"unknown-column"'* ]] || fail "LSP accepts explicit derived-table column aliases"
lsp_table_name_after_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-name-after-alias.sql","languageId":"sql","version":1,"text":"SELECT Info.id FROM dbo.Info i;"}}}'
lsp_table_name_after_alias_output="$(lsp_run "$lsp_initialize" "$lsp_table_name_after_alias")"
[[ "$lsp_table_name_after_alias_output" == *'"code":"table-name-after-alias"'* && "$lsp_table_name_after_alias_output" == *'known as alias'* ]] || fail "LSP explains table names cannot be used after an alias"
lsp_alias_code_action='{"jsonrpc":"2.0","id":11,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-name-after-alias.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}},"context":{"diagnostics":[{"source":"sqldeps","code":"table-name-after-alias","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}},"message":"The multi-part identifier Info.id could not be bound: table Info is known as alias \u0027i\u0027 in this query."}]}}}'
lsp_alias_code_action_output="$(lsp_run "$lsp_initialize" "$lsp_table_name_after_alias" "$lsp_alias_code_action")"
[[ "$lsp_alias_code_action_output" == *'"title":"Replace table name with alias \u0027i\u0027"'* && "$lsp_alias_code_action_output" == *'"newText":"i.id"'* && "$lsp_alias_code_action_output" == *'"start":{"line":0,"character":7}'* && "$lsp_alias_code_action_output" == *'"end":{"line":0,"character":14}'* ]] || fail "LSP preserves the alias replacement range"
lsp_table_name_after_alias_fixed='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-name-after-alias.sql","version":2},"contentChanges":[{"text":"SELECT i.id FROM dbo.Info i;"}]}}'
lsp_alias_fixed_output="$(lsp_run "$lsp_initialize" "$lsp_table_name_after_alias" "$lsp_table_name_after_alias_fixed")"
lsp_alias_diagnostic_count="$( (printf '%s' "$lsp_alias_fixed_output" | grep -o '"code":"table-name-after-alias"' || true) | wc -l | tr -d ' ')"
[[ "$lsp_alias_diagnostic_count" == "1" ]] || fail "LSP re-analyzes alias quick-fix edits"
lsp_schema_qualified_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema-qualified-alias.sql","languageId":"sql","version":1,"text":"SELECT dbo.Info.id FROM dbo.Info i;"}}}'
lsp_schema_qualified_alias_output="$(lsp_run "$lsp_initialize" "$lsp_schema_qualified_alias")"
[[ "$lsp_schema_qualified_alias_output" == *'"code":"table-name-after-alias"'* && "$lsp_schema_qualified_alias_output" == *'known as alias'* ]] || fail "LSP recognizes schema-qualified table names after an alias"
lsp_schema_qualified_alias_action='{"jsonrpc":"2.0","id":12,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema-qualified-alias.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":18}},"context":{"diagnostics":[{"source":"sqldeps","code":"table-name-after-alias","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":18}},"message":"The multi-part identifier dbo.Info.id could not be bound: table dbo.Info is known as alias \u0027i\u0027 in this query."}]}}}'
lsp_schema_qualified_alias_action_output="$(lsp_run "$lsp_initialize" "$lsp_schema_qualified_alias" "$lsp_schema_qualified_alias_action")"
[[ "$lsp_schema_qualified_alias_action_output" == *'"newText":"i.id"'* ]] || fail "LSP replaces a schema-qualified table name with its alias"
lsp_duplicate_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-alias.sql","languageId":"sql","version":1,"text":"SELECT i.id FROM dbo.Info i JOIN dbo.OtherInfo i ON 1 = 1;"}}}'
lsp_duplicate_alias_output="$(lsp_run "$lsp_initialize" "$lsp_duplicate_alias")"
[[ "$lsp_duplicate_alias_output" == *'"code":"duplicate-alias"'* && "$lsp_duplicate_alias_output" == *'Duplicate table alias'* ]] || fail "LSP reports duplicate table aliases"
