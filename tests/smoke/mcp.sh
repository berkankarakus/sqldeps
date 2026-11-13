mcp_output="$(printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{}}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_objects","arguments":{"pattern":"update","limit":2}}}' \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"lint","arguments":{"limit":1}}}' \
  '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"query_builder","arguments":{"table":"tbl_a"}}}' \
  '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"explain","arguments":{"rule":"select-star"}}}' \
  '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"analyze","arguments":{"source_file":"live.sql","text":"SELECT * FROM dbo.tbl_a;"}}}' \
  '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"parameterize_dynamic_sql","arguments":{"expression":"N\u0027SELECT * FROM dbo.tbl_a WHERE id = \u0027 + @id","declarations":"DECLARE @id INT = 1;"}}}' \
  '{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"parameterize_dynamic_sql","arguments":{"expression":"N\u0027SELECT * FROM \u0027 + @tableName","declarations":"DECLARE @tableName NVARCHAR(128);"}}}' \
  '{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"drift","arguments":{"limit":2}}}' \
  '{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"test_skeleton","arguments":{"procedure":"dbo.sp_Report"}}}' \
  '{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"test_skeleton","arguments":{"procedure":"dbo.sp_UpdatePrices"}}}' \
  '{"jsonrpc":"2.0","id":13,"method":"tools/call","params":{"name":"who_updates","arguments":{"table":"dbo.tbl_a","column":"price","limit":2}}}' \
  '{"jsonrpc":"2.0","id":14,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live.sql","text":"SELECT * FROM dbo.tbl_a;"}}}' \
  '{"jsonrpc":"2.0","id":15,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live-index.sql","text":"CREATE TABLE dbo.Filtered (id INT, title NVARCHAR(50)); CREATE INDEX IX_Filtered_Id ON dbo.Filtered (id); SELECT id FROM dbo.Filtered WHERE title = N\u0027x\u0027;"}}}' \
  '{"jsonrpc":"2.0","id":16,"method":"tools/call","params":{"name":"code_actions","arguments":{"source_file":"live-dynamic.sql","text":"DECLARE @id INT = 1; EXEC(N\u0027SELECT * FROM dbo.tbl_a WHERE id = \u0027 + @id);"}}}' \
  '{"jsonrpc":"2.0","id":17,"method":"tools/call","params":{"name":"refactor_candidates","arguments":{"source_file":"live.sql","text":"SELECT * FROM dbo.tbl_a;"}}}' \
  '{"jsonrpc":"2.0","id":18,"method":"tools/call","params":{"name":"refactor_candidates","arguments":{"source_file":"live-dynamic.sql","text":"DECLARE @id INT = 1; EXEC(N\u0027SELECT * FROM dbo.tbl_a WHERE id = \u0027 + @id);"}}}' \
  '{"jsonrpc":"2.0","id":19,"method":"tools/call","params":{"name":"query_builder","arguments":{}}}' \
  '{"jsonrpc":"2.0","id":20,"method":"resources/list","params":{}}' \
  '{"jsonrpc":"2.0","id":21,"method":"resources/read","params":{"uri":"sqldeps://diagnostic/select-star"}}' \
  '{"jsonrpc":"2.0","id":22,"method":"tools/call","params":{"name":"callers","arguments":{"object":"dbo.Totals","recursive":true,"depth":10,"limit":5}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  | "$sqldeps_app" mcp --db "$database")"

[[ "$(printf '%s\n' "$mcp_output" | wc -l | tr -d ' ')" == "22" ]] || fail "MCP does not answer notifications"
[[ "$mcp_output" == *'"serverInfo":{"name":"sqldeps"'* && "$mcp_output" == *'"resources":{"subscribe":false,"listChanged":false}'* ]] || fail "MCP initialize returns server capabilities"
[[ "$mcp_output" == *'"name":"find_objects"'* && "$mcp_output" == *'"name":"table_columns"'* && "$mcp_output" == *'"name":"query_builder"'* \
    && "$mcp_output" == *'"name":"who_updates"'* && "$mcp_output" == *'"name":"parameterize_dynamic_sql"'* \
    && "$mcp_output" == *'"name":"test_skeleton"'* && "$mcp_output" == *'"name":"code_actions"'* \
    && "$mcp_output" == *'"name":"refactor_candidates"'* && "$mcp_output" == *'"name":"callers"'* ]] || fail "MCP lists named tools"
[[ "$mcp_output" == *'"structuredContent":{"rows"'* && "$mcp_output" == *'dbo.sp_UpdatePrices'* ]] || fail "MCP returns structured object query results"
[[ "$mcp_output" == *'"id":4,"result"'* && "$mcp_output" == *'"severity"'* ]] || fail "MCP returns lint results"
[[ "$mcp_output" == *'"id":5,"result"'* && "$mcp_output" == *'"object_name":"dbo.tbl_a"'* && "$mcp_output" == *'"sql":"SELECT\n'* ]] || fail "MCP returns a parse-validated query preview"
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
[[ "$mcp_output" == *'"id":17,"result"'* && "$mcp_output" == *'"candidate_count":1'* \
    && "$mcp_output" == *'"status":"complete"'* && "$mcp_output" == *'"preview"'* \
    && "$mcp_output" == *'"writes_file":false'* ]] || fail "MCP returns safe refactor candidate previews"
[[ "$mcp_output" == *'"id":18,"result"'* && "$mcp_output" == *'"rule":"dynamic-sql-concatenation"'* \
    && "$mcp_output" == *'sp_executesql'* && "$mcp_output" == *'"writes_file":false'* ]] || fail "MCP returns the safe dynamic SQL refactor candidate"
[[ "$mcp_output" == *'"id":19,"error"'* && "$mcp_output" == *'MCP argument'* \
    && "$mcp_output" == *'table'* && "$mcp_output" == *'required'* ]] || fail "MCP rejects incomplete query builder arguments"
[[ "$mcp_output" == *'"id":20,"result":{"resources"'* && "$mcp_output" == *'sqldeps://diagnostic/select-star'* ]] || fail "MCP lists diagnostic evidence resources"
[[ "$mcp_output" == *'"id":21,"result":{"contents"'* && "$mcp_output" == *'"rule":"select-star"'* \
    && "$mcp_output" == *'learn.microsoft.com'* ]] || fail "MCP reads diagnostic evidence resources"
[[ "$mcp_output" == *'"id":22,"result"'* && "$mcp_output" == *'"source":"dbo.sp_C"'* \
    && "$mcp_output" == *'"path"'* ]] || fail "MCP returns recursive caller edge evidence"

unsupported_protocol="$(printf '%s\n' '{"jsonrpc":"2.0","id":22,"method":"initialize","params":{"protocolVersion":"2099-01-01"}}' | "$sqldeps_app" mcp --db "$database")"
[[ "$unsupported_protocol" == *'"id":22,"error":{"code":-32602'* && "$unsupported_protocol" == *'Unsupported MCP protocol version'* ]] || fail "MCP rejects unsupported protocol versions"

invalid_tool="$(printf '%s\n' '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"arbitrary_sql","arguments":{"sql":"select 1"}}}' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_tool" == *'"code":-32602'* && "$invalid_tool" == *"Unknown MCP tool"* ]] || fail "MCP rejects arbitrary tools"

invalid_source_file="$(printf '%s\n' '{"jsonrpc":"2.0","id":23,"method":"tools/call","params":{"name":"analyze","arguments":{"source_file":true,"text":"SELECT 1;"}}}' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_source_file" == *'"id":23,"error":{"code":-32602'* && "$invalid_source_file" == *"source_file"* && "$invalid_source_file" == *"string"* ]] || fail "MCP rejects invalid optional string arguments"

invalid_json="$(printf '%s\n' '{' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_json" == *'"code":-32700'* && "$invalid_json" == *'Invalid JSON'* ]] || fail "MCP rejects malformed JSON messages"

unknown_method="$(printf '%s\n' '{"jsonrpc":"2.0","id":6,"method":"workspace/execute","params":{}}' | "$sqldeps_app" mcp --db "$database")"
[[ "$unknown_method" == *'"id":6,"error":{"code":-32601'* && "$unknown_method" == *'Unknown MCP method'* ]] || fail "MCP rejects unknown methods"

invalid_version="$(printf '%s\n' '{"jsonrpc":"1.0","id":7,"method":"ping"}' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_version" == *'"id":7,"error":{"code":-32600'* ]] || fail "MCP rejects invalid JSON-RPC versions"

invalid_envelope="$(printf '%s\n' '[]' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_envelope" == *'"id":null,"error":{"code":-32600'* ]] || fail "MCP rejects non-object JSON-RPC envelopes"

invalid_arguments="$(printf '%s\n' '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"query_builder","arguments":[]}}' | "$sqldeps_app" mcp --db "$database")"
[[ "$invalid_arguments" == *'"id":8,"error":{"code":-32602'* && "$invalid_arguments" == *'MCP argument'* && "$invalid_arguments" == *'table'* && "$invalid_arguments" == *'required'* ]] || fail "MCP rejects non-object tool arguments"
