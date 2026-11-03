lsp_hover_document='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Info WITH (NOLOCK);"}}}'
lsp_hover_request='{"jsonrpc":"2.0","id":52,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/sqldeps-diagnostic-hover.sql"},"position":{"line":0,"character":7}}}'
lsp_hover_output="$(lsp_run "$lsp_initialize" "$lsp_hover_document" "$lsp_hover_request")"
[[ "$lsp_hover_output" == *'"id":52'* && "$lsp_hover_output" == *'sqldeps: select-star'* && "$lsp_hover_output" == *'Confidence'* && "$lsp_hover_output" == *'live-document'* && "$lsp_hover_output" == *'SELECT * makes result shape'* && "$lsp_hover_output" == *'Suggested direction'* ]] || fail "LSP explains a lint diagnostic on hover"

printf '%s\n' 'diagnostic-hover: ok'
