mcp_output="$(printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{}}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_objects","arguments":{"pattern":"update","limit":2}}}' \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"lint","arguments":{"limit":1}}}' \
  '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"query_builder","arguments":{"table":"upInfo"}}}' \
  '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"explain","arguments":{"rule":"select-star"}}}' \
  '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"analyze","arguments":{"source_file":"live.sql","text":"SELECT * FROM dbo.upInfo;"}}}' \
  '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"parameterize_dynamic_sql","arguments":{"expression":"N\u0027SELECT * FROM dbo.upInfo WHERE id = \u0027 + @id","declarations":"DECLARE @id INT = 1;"}}}' \
  '{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"parameterize_dynamic_sql","arguments":{"expression":"N\u0027SELECT * FROM \u0027 + @tableName","declarations":"DECLARE @tableName NVARCHAR(128);"}}}' \
  '{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"drift","arguments":{"limit":2}}}' \
  '{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"test_skeleton","arguments":{"procedure":"dbo.sp_Report"}}}' \
  '{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"test_skeleton","arguments":{"procedure":"dbo.sp_UpdatePrices"}}}' \
  '{"jsonrpc":"2.0","id":13,"method":"tools/call","params":{"name":"who_updates","arguments":{"table":"dbo.upInfo","column":"price","limit":2}}}' \
  '{"jsonrpc":"2.0","id":14,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live.sql","text":"SELECT * FROM dbo.upInfo;"}}}' \
  '{"jsonrpc":"2.0","id":15,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live-index.sql","text":"CREATE TABLE dbo.Filtered (id INT, title NVARCHAR(50)); CREATE INDEX IX_Filtered_Id ON dbo.Filtered (id); SELECT id FROM dbo.Filtered WHERE title = N\u0027x\u0027;"}}}' \
  '{"jsonrpc":"2.0","id":16,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live-dynamic.sql","text":"DECLARE @id INT = 1; EXEC(N\u0027SELECT * FROM dbo.upInfo WHERE id = \u0027 + @id);"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  | dotnet run -c Release --no-build -- mcp --db "$database")"

[[ "$(printf '%s\n' "$mcp_output" | wc -l | tr -d ' ')" == "16" ]] || fail "MCP does not answer notifications"
[[ "$mcp_output" == *'"serverInfo":{"name":"sqldeps"'* ]] || fail "MCP initialize returns server information"
[[ "$mcp_output" == *'"name":"find_objects"'* && "$mcp_output" == *'"name":"table_columns"'* && "$mcp_output" == *'"name":"query_builder"'* \
    && "$mcp_output" == *'"name":"who_updates"'* && "$mcp_output" == *'"name":"parameterize_dynamic_sql"'* \
    && "$mcp_output" == *'"name":"test_skeleton"'* && "$mcp_output" == *'"name":"code_actions"'* ]] || fail "MCP lists named tools"
[[ "$mcp_output" == *'"structuredContent":{"rows"'* && "$mcp_output" == *'dbo.sp_UpdatePrices'* ]] || fail "MCP returns structured object query results"
[[ "$mcp_output" == *'"id":4,"result"'* && "$mcp_output" == *'"severity"'* ]] || fail "MCP returns lint results"
[[ "$mcp_output" == *'"id":5,"result"'* && "$mcp_output" == *'"object_name":"dbo.upInfo"'* && "$mcp_output" == *'"sql":"SELECT\n'* ]] || fail "MCP returns a parse-validated query preview"
[[ "$mcp_output" == *'"id":6,"result"'* && "$mcp_output" == *'"rule":"select-star"'* && "$mcp_output" == *'"default_severity":"info"'* ]] || fail "MCP returns diagnostic rule explanations"
[[ "$mcp_output" == *'"id":7,"result"'* && "$mcp_output" == *'"source_file":"live.sql"'* && "$mcp_output" == *'"rule":"select-star"'* ]] || fail "MCP analyzes unsaved SQL with active metadata"
[[ "$mcp_output" == *'"id":8,"result"'* && "$mcp_output" == *'@__sqldeps_0 INT'* && "$mcp_output" == *'"writes_file":false'* ]] || fail "MCP returns a safe dynamic SQL parameter preview"
[[ "$mcp_output" == *'"id":9,"error"'* && "$mcp_output" == *'could not be safely parameterized'* ]] || fail "MCP rejects unsafe dynamic SQL parameterization"
[[ "$mcp_output" == *'"id":10,"result"'* && "$mcp_output" == *'"structuredContent":{"rows":[]}'* ]] || fail "MCP reports a current indexed source folder"
[[ "$mcp_output" == *'"id":11,"result"'* && "$mcp_output" == *'"writes_file":false'* && "$mcp_output" == *'"sql":"EXEC '* ]] || fail "MCP returns a procedure test skeleton preview"
[[ "$mcp_output" == *'"id":12,"error"'* && "$mcp_output" == *'OUTPUT parameters require caller variables'* ]] || fail "MCP rejects incomplete procedure test skeleton metadata"
[[ "$mcp_output" == *'"id":13,"result"'* && "$mcp_output" == *'"structuredContent":{"rows"'* ]] || fail "MCP returns column update writers"
[[ "$mcp_output" == *'"id":14,"result"'* && "$mcp_output" == *'select-star'* \
    && "$mcp_output" == *'"writes_file":false'* ]] || fail "MCP returns safe code-action previews"
[[ "$mcp_output" == *'"id":15,"result"'* && "$mcp_output" == *'sqldeps.index-suggestion'* \
    && "$mcp_output" == *'"writesFile":false'* ]] || fail "MCP returns safe index suggestion previews"
[[ "$mcp_output" == *'"id":16,"result"'* && "$mcp_output" == *'dynamic-sql-concatenation'* \
    && "$mcp_output" == *'"references"'* && "$mcp_output" == *'learn.microsoft.com'* ]] || fail "MCP preserves evidence in dynamic SQL code-action previews"

invalid_tool="$(printf '%s\n' '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"arbitrary_sql","arguments":{"sql":"select 1"}}}' | dotnet run -c Release --no-build -- mcp --db "$database")"
[[ "$invalid_tool" == *'"code":-32602'* && "$invalid_tool" == *"Unknown MCP tool"* ]] || fail "MCP rejects arbitrary tools"
