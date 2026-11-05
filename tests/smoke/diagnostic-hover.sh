lsp_hover_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WITH (NOLOCK);"}}}'
lsp_hover_request='{"jsonrpc":"2.0","id":52,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql"},"position":{"line":0,"character":7}}}'
lsp_hover_output="$(lsp_run "$lsp_initialize" "$lsp_hover_document" "$lsp_hover_request")"
[[ "$lsp_hover_output" == *'"id":52'* && "$lsp_hover_output" == *'sqldeps: select-star'* && "$lsp_hover_output" == *'Confidence'* && "$lsp_hover_output" == *'live-document'* && "$lsp_hover_output" == *'SELECT * makes result shape'* && "$lsp_hover_output" == *'Suggested direction'* ]] || fail "LSP explains a lint diagnostic on hover"

lsp_multi_hover_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-multi-hover.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id INT, @name NVARCHAR(20), @title VARCHAR(30) AS SELECT @id;\nGO\nEXEC dbo.DoWork @id = 1;"}}}'
lsp_multi_hover_request='{"jsonrpc":"2.0","id":53,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-multi-hover.sql"},"position":{"line":2,"character":5}}}'
lsp_multi_hover_output="$(lsp_run "$lsp_initialize" "$lsp_multi_hover_document" "$lsp_multi_hover_request")"
[[ "$lsp_multi_hover_output" == *'"id":53'* && "$lsp_multi_hover_output" == *'@name'* && "$lsp_multi_hover_output" == *'@title'* && "$lsp_multi_hover_output" == *'---'* ]] || fail "LSP hover shows all diagnostics at an overlapping range"

printf '%s\n' 'diagnostic-hover: ok'
