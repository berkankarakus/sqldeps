mcp_output="$(printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{}}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_objects","arguments":{"pattern":"update","limit":2}}}' \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"lint","arguments":{"limit":1}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  | dotnet run -c Release --no-build -- mcp --db "$database")"

[[ "$(printf '%s\n' "$mcp_output" | wc -l | tr -d ' ')" == "4" ]] || fail "MCP does not answer notifications"
[[ "$mcp_output" == *'"serverInfo":{"name":"sqldeps"'* ]] || fail "MCP initialize returns server information"
[[ "$mcp_output" == *'"name":"find_objects"'* && "$mcp_output" == *'"name":"table_columns"'* ]] || fail "MCP lists named tools"
[[ "$mcp_output" == *'"structuredContent":{"rows"'* && "$mcp_output" == *'dbo.sp_UpdatePrices'* ]] || fail "MCP returns structured object query results"
[[ "$mcp_output" == *'"id":4,"result"'* && "$mcp_output" == *'"severity"'* ]] || fail "MCP returns lint results"

invalid_tool="$(printf '%s\n' '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"arbitrary_sql","arguments":{"sql":"select 1"}}}' | dotnet run -c Release --no-build -- mcp --db "$database")"
[[ "$invalid_tool" == *'"code":-32602'* && "$invalid_tool" == *"Unknown MCP tool"* ]] || fail "MCP rejects arbitrary tools"
