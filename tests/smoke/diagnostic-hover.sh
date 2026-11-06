lsp_hover_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WITH (NOLOCK);"}}}'
lsp_hover_request='{"jsonrpc":"2.0","id":52,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql"},"position":{"line":0,"character":7}}}'
lsp_hover_output="$(lsp_run "$lsp_initialize" "$lsp_hover_document" "$lsp_hover_request")"
[[ "$lsp_hover_output" == *'"id":52'* && "$lsp_hover_output" == *'sqldeps: select-star'* && "$lsp_hover_output" == *'Confidence'* && "$lsp_hover_output" == *'live-document'* && "$lsp_hover_output" == *'SELECT * makes result shape'* && "$lsp_hover_output" == *'Suggested direction'* ]] || fail "LSP explains a lint diagnostic on hover"

lsp_multi_hover_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-multi-hover.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id INT, @name NVARCHAR(20), @title VARCHAR(30) AS SELECT @id;\nGO\nEXEC dbo.DoWork @id = 1;"}}}'
lsp_multi_hover_request='{"jsonrpc":"2.0","id":53,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-multi-hover.sql"},"position":{"line":2,"character":5}}}'
lsp_multi_hover_output="$(lsp_run "$lsp_initialize" "$lsp_multi_hover_document" "$lsp_multi_hover_request")"
[[ "$lsp_multi_hover_output" == *'"id":53'* && "$lsp_multi_hover_output" == *'@name'* && "$lsp_multi_hover_output" == *'@title'* && "$lsp_multi_hover_output" == *'---'* ]] || fail "LSP hover shows all diagnostics at an overlapping range"

lsp_explanation_action='{"jsonrpc":"2.0","id":54,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"context":{"diagnostics":[{"source":"sqldeps","code":"duplicate-definition","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"message":"The same object has multiple definitions."}]}}}'
lsp_explanation_action_output="$(lsp_run "$lsp_initialize" "$lsp_hover_document" "$lsp_explanation_action")"
[[ "$lsp_explanation_action_output" == *'Explain sqldeps \u0027duplicate-definition\u0027'* && "$lsp_explanation_action_output" == *'"command":"sqldeps.explain-diagnostic"'* && "$lsp_explanation_action_output" == *'"writesFile":false'* ]] || fail "LSP exposes explanation actions for ordinary diagnostics"

lsp_repository_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT);\nGO\nCREATE PROCEDURE dbo.Reader AS SELECT id FROM dbo.Info;"}}}'
lsp_repository_action='{"jsonrpc":"2.0","id":55,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql"},"range":{"start":{"line":0,"character":13},"end":{"line":0,"character":21}},"context":{"diagnostics":[]}}}'
lsp_repository_action_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_action")"
[[ "$lsp_repository_action_output" == *'Show repository context for dbo.Info'* && "$lsp_repository_action_output" == *'"command":"sqldeps.repository-context"'* ]] || fail "LSP offers repository context for selected indexed objects"
lsp_repository_command='{"jsonrpc":"2.0","id":56,"method":"workspace/executeCommand","params":{"command":"sqldeps.repository-context","arguments":["dbo.Info"]}}'
lsp_repository_command_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_command")"
[[ "$lsp_repository_command_output" == *'Repository context: dbo.Info'* && "$lsp_repository_command_output" == *'Referenced by: dbo.Reader (SELECT)'* && "$lsp_repository_command_output" == *'Affected by changes: dbo.Reader'* ]] || fail "LSP returns repository dependency and impact context"
lsp_repository_hover='{"jsonrpc":"2.0","id":57,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql"},"position":{"line":0,"character":16}}}'
lsp_repository_hover_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_hover")"
[[ "$lsp_repository_hover_output" == *'Repository context'* && "$lsp_repository_hover_output" == *'Affected by changes'* ]] || fail "LSP includes repository context counts in object hover"

printf '%s\n' 'diagnostic-hover: ok'
