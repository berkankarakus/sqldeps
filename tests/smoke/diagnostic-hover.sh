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
[[ "$lsp_explanation_action_output" == *'Explain sqldeps \u0027duplicate-definition\u0027'* && "$lsp_explanation_action_output" == *'"command":"sqldeps.explain-diagnostic"'* && "$lsp_explanation_action_output" == *'"writesFile":false'* && "$lsp_explanation_action_output" == *'"applicability":'* ]] || fail "LSP exposes metadata-complete explanation actions for ordinary diagnostics"

lsp_repository_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT);\nGO\nCREATE PROCEDURE dbo.Reader AS SELECT id FROM dbo.Info;"}}}'
lsp_repository_action='{"jsonrpc":"2.0","id":55,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql"},"range":{"start":{"line":0,"character":13},"end":{"line":0,"character":21}},"context":{"diagnostics":[]}}}'
lsp_repository_action_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_action")"
[[ "$lsp_repository_action_output" == *'Show repository context for dbo.Info'* && "$lsp_repository_action_output" == *'"command":"sqldeps.repository-context"'* && "$lsp_repository_action_output" == *'"applicability":'* ]] || fail "LSP offers metadata-complete repository context for selected indexed objects"
lsp_repository_command='{"jsonrpc":"2.0","id":56,"method":"workspace/executeCommand","params":{"command":"sqldeps.repository-context","arguments":["dbo.Info"]}}'
lsp_repository_command_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_command")"
[[ "$lsp_repository_command_output" == *'Repository context: dbo.Info'* && "$lsp_repository_command_output" == *'Referenced by: dbo.Reader (SELECT)'* && "$lsp_repository_command_output" == *'Affected by changes: dbo.Reader'* ]] || fail "LSP returns repository dependency and impact context"
lsp_repository_hover='{"jsonrpc":"2.0","id":57,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-repository-context.sql"},"position":{"line":0,"character":16}}}'
lsp_repository_hover_output="$(lsp_run "$lsp_initialize" "$lsp_repository_document" "$lsp_repository_hover")"
[[ "$lsp_repository_hover_output" == *'Repository context'* && "$lsp_repository_hover_output" == *'Affected by changes'* ]] || fail "LSP includes repository context counts in object hover"

lsp_dependency_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dependency-actions.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT);\nGO\nCREATE PROCEDURE dbo.Reader AS SELECT id FROM dbo.Info;\nGO\nCREATE PROCEDURE dbo.Writer AS UPDATE dbo.Info SET id = id;\nGO\nSELECT dbo.Info.id FROM dbo.Info;"}}}'
lsp_dependency_action='{"jsonrpc":"2.0","id":58,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dependency-actions.sql"},"range":{"start":{"line":0,"character":13},"end":{"line":0,"character":21}},"context":{"diagnostics":[]}}}'
lsp_dependency_action_output="$(lsp_run "$lsp_initialize" "$lsp_dependency_document" "$lsp_dependency_action")"
[[ "$lsp_dependency_action_output" == *'Show who writes dbo.Info'* && "$lsp_dependency_action_output" == *'"command":"sqldeps.who-writes"'* && "$lsp_dependency_action_output" == *'Show who reads dbo.Info'* && "$lsp_dependency_action_output" == *'"command":"sqldeps.who-reads"'* && "$lsp_dependency_action_output" == *'Show impact of changing dbo.Info'* && "$lsp_dependency_action_output" == *'"command":"sqldeps.impact"'* ]] || fail "LSP offers dependency context actions for selected indexed objects"
lsp_column_action='{"jsonrpc":"2.0","id":62,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dependency-actions.sql"},"range":{"start":{"line":6,"character":7},"end":{"line":6,"character":18}},"context":{"diagnostics":[]}}}'
lsp_column_action_output="$(lsp_run "$lsp_initialize" "$lsp_dependency_document" "$lsp_column_action")"
[[ "$lsp_column_action_output" == *'Show column writers for dbo.Info.id'* && "$lsp_column_action_output" == *'"command":"sqldeps.column-writers"'* ]] || fail "LSP offers column writer actions for selected indexed columns"
lsp_who_writes='{"jsonrpc":"2.0","id":59,"method":"workspace/executeCommand","params":{"command":"sqldeps.who-writes","arguments":["dbo.Info"]}}'
lsp_who_writes_output="$(lsp_run "$lsp_initialize" "$lsp_dependency_document" "$lsp_who_writes")"
[[ "$lsp_who_writes_output" == *'Who writes: dbo.Info'* && "$lsp_who_writes_output" == *'dbo.Writer (UPDATE)'* ]] || fail "LSP reports objects that write to a selected object"
lsp_who_reads='{"jsonrpc":"2.0","id":60,"method":"workspace/executeCommand","params":{"command":"sqldeps.who-reads","arguments":["dbo.Info"]}}'
lsp_who_reads_output="$(lsp_run "$lsp_initialize" "$lsp_dependency_document" "$lsp_who_reads")"
[[ "$lsp_who_reads_output" == *'Who reads: dbo.Info'* && "$lsp_who_reads_output" == *'dbo.Reader (SELECT)'* ]] || fail "LSP reports objects that read a selected object"
lsp_impact='{"jsonrpc":"2.0","id":61,"method":"workspace/executeCommand","params":{"command":"sqldeps.impact","arguments":["dbo.Info"]}}'
lsp_impact_output="$(lsp_run "$lsp_initialize" "$lsp_dependency_document" "$lsp_impact")"
[[ "$lsp_impact_output" == *'Impact of changing: dbo.Info'* && "$lsp_impact_output" == *'dbo.Reader (depth 1)'* && "$lsp_impact_output" == *'dbo.Writer (depth 1)'* ]] || fail "LSP reports the impact of changing a selected object"

printf '%s\n' 'diagnostic-hover: ok'
