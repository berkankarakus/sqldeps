usage_output="$("$sqldeps_app" 2>&1 || true)"
[[ "$usage_output" == *"sqldeps lsp"* ]] || fail "usage lists the LSP server"
lsp_oversized_output="$(printf 'Content-Length: 67108865\r\n\r\n' | "$sqldeps_app" lsp 2>&1 || true)"
[[ "$lsp_oversized_output" == *'exceeds the 67108864 byte limit'* ]] || fail "LSP rejects oversized JSON-RPC bodies"
lsp_invalid_header_output="$(printf 'Content-Length: nope\r\n\r\n' | "$sqldeps_app" lsp 2>&1 || true)"
[[ "$lsp_invalid_header_output" == *'invalid Content-Length header'* ]] || fail "LSP rejects malformed Content-Length headers"
lsp_duplicate_header_output="$(printf 'Content-Length: 0\r\nContent-Length: 0\r\n\r\n' | "$sqldeps_app" lsp 2>&1 || true)"
[[ "$lsp_duplicate_header_output" == *'duplicate Content-Length headers'* ]] || fail "LSP rejects duplicate Content-Length headers"
lsp_header_limit_output="$(printf 'X-Header: %*s\r\n\r\n' 8192 '' | "$sqldeps_app" lsp 2>&1 || true)"
[[ "$lsp_header_limit_output" == *'header exceeds the 8192 byte limit'* ]] || fail "LSP rejects oversized JSON-RPC headers"
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
lsp_invalid_request_output="$({ lsp_frame "$lsp_initialize"; lsp_frame "$lsp_invalid_request"; } | "$sqldeps_app" lsp 2>&1 || true)"
[[ "$lsp_invalid_request_output" == *'"id":2,"error":{"code":-32602'* ]] || fail "LSP returns an error for invalid request parameters"

lsp_partial_text_file="$smoke_work/lsp-partial.sql"
lsp_partial_document_file="$smoke_work/lsp-partial-document.json"
printf 'SELECT 1;\n' > "$lsp_partial_text_file"
dd if=/dev/zero bs=1048576 count=17 2>/dev/null | tr '\0' ' ' >> "$lsp_partial_text_file"
jq -cn --arg uri 'file:///tmp/sqldeps-partial.sql' --rawfile text "$lsp_partial_text_file" \
  '{jsonrpc:"2.0",method:"textDocument/didOpen",params:{textDocument:{uri:$uri,languageId:"sql",version:1,text:$text}}}' \
  > "$lsp_partial_document_file"
lsp_partial_output="$(
  { lsp_frame "$lsp_initialize"; lsp_frame_file "$lsp_partial_document_file"; } \
    | "$sqldeps_app" lsp 2>&1 || true
)"
[[ "$lsp_partial_output" == *'"code":"partial-analysis"'* && "$lsp_partial_output" == *'full-analysis limit'* ]] \
  || fail "LSP reports partial analysis instead of a clean result for oversized documents"
