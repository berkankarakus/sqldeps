usage_output="$(dotnet run -c Release --no-build -- 2>&1 || true)"
[[ "$usage_output" == *"sqldeps lsp"* ]] || fail "usage lists the LSP server"
lsp_oversized_output="$(printf 'Content-Length: 16777217\r\n\r\n' | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_oversized_output" == *'exceeds the 16777216 byte limit'* ]] || fail "LSP rejects oversized JSON-RPC bodies"
lsp_invalid_header_output="$(printf 'Content-Length: nope\r\n\r\n' | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_invalid_header_output" == *'invalid Content-Length header'* ]] || fail "LSP rejects malformed Content-Length headers"
lsp_duplicate_header_output="$(printf 'Content-Length: 0\r\nContent-Length: 0\r\n\r\n' | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_duplicate_header_output" == *'duplicate Content-Length headers'* ]] || fail "LSP rejects duplicate Content-Length headers"
lsp_malformed_json='{ "jsonrpc": "2.0",'
lsp_malformed_json_output="$(lsp_run "$lsp_malformed_json" "$lsp_initialize")"
[[ "$lsp_malformed_json_output" == *'"capabilities"'* ]] || fail "LSP continues after a malformed JSON message"
lsp_unicode_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unicode-framing.sql","languageId":"sql","version":1,"text":"SELECT N\u0027İstanbul\u0027 AS city FROM dbo.Info WITH (NOLOCK);"}}}'
lsp_unicode_output="$(lsp_run "$lsp_initialize" "$lsp_unicode_open")"
[[ "$lsp_unicode_output" == *'"capabilities"'* && "$lsp_unicode_output" == *'"code":"nolock"'* ]] || fail "LSP frames UTF-8 SQL using byte Content-Length"
lsp_invalid_version='{"jsonrpc":"1.0","id":40,"method":"initialize","params":{}}'
lsp_invalid_version_output="$(lsp_run "$lsp_invalid_version")"
[[ "$lsp_invalid_version_output" == *'"id":40,"error":{"code":-32600'* ]] || fail "LSP rejects requests with an invalid JSON-RPC version"
lsp_invalid_request='{"jsonrpc":"2.0","id":2,"method":"textDocument/hover","params":{}}'
lsp_invalid_request_output="$({ lsp_frame "$lsp_initialize"; lsp_frame "$lsp_invalid_request"; } | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_invalid_request_output" == *'"id":2,"error":{"code":-32602'* ]] || fail "LSP returns an error for invalid request parameters"
