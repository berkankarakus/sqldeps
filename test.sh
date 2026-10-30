#!/usr/bin/env bash
# Builds sqldeps, indexes fixtures/ and checks the answers to the questions the index exists for.
set -euo pipefail
cd "$(dirname "$0")"

database="$(mktemp -d)/deps.sqlite"
dotnet build -c Release -nologo -v q >/dev/null
dotnet run -c Release --no-build -- index fixtures --db "$database" >/dev/null

fail() { echo "FAIL: $*" >&2; exit 1; }

expect_on() {
  local file="$1" description="$2" sql="$3" expected="$4" actual
  actual="$(sqlite3 "$file" "$sql")"
  [[ "$actual" == "$expected" ]] || fail "$description (expected: $expected, got: $actual)"
}

expect() { expect_on "$database" "$@"; }

index() { dotnet run -c Release --no-build -- index "$@"; }

expect "UPDATE through an alias resolves to the table" \
  "SELECT group_concat(DISTINCT target_object) FROM dependencies WHERE source_object = 'dbo.sp_UpdatePrices' AND operation = 'UPDATE'" \
  "dbo.upInfo"
expect "an alias never shows up as a table" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object = 'dbo.u'" "0"
expect "who updates dbo.upInfo, whatever the case of the name" \
  "SELECT group_concat(source_object) FROM (SELECT DISTINCT source_object FROM dependencies WHERE target_object = 'DBO.UPINFO' AND operation = 'UPDATE' ORDER BY 1)" \
  "dbo.sp_Fiyat_Güncelle,dbo.sp_Tricky,dbo.sp_UpdatePrices"
expect "temp tables and CTEs are not dependencies" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object LIKE '#%' OR target_object IN ('dbo.recent', 'dbo.work')" "0"
expect "MERGE records the merge and each of its actions" \
  "SELECT group_concat(operation) FROM (SELECT DISTINCT operation FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.upInfo' AND operation IN ('MERGE', 'INSERT', 'DELETE') ORDER BY 1)" \
  "DELETE,INSERT,MERGE"
expect "SELECT INTO writes the new table" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.PriceCopy' AND operation = 'INSERT'" "1"
expect "TRUNCATE is recorded" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object = 'dbo.PriceLog' AND operation = 'TRUNCATE'" "1"
expect "constant EXEC SQL is parsed as a dynamic read" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.DynamicExec' AND operation = 'SELECT' AND via_dynamic = 1" "1"
expect "constant sp_executesql SQL is parsed as a dynamic write" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.DynamicExecute' AND operation = 'UPDATE' AND via_dynamic = 1" "1"
expect "constant SQL concatenation is parsed as a dynamic read" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.DynamicConcat' AND operation = 'SELECT' AND via_dynamic = 1" "1"
expect "variable dynamic SQL remains unresolved" \
  "SELECT COUNT(*) FROM dependencies WHERE operation = 'EXEC_DYNAMIC' AND target_object = '(dynamic sql)'" "1"
expect "a name without a schema is read as dbo" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_Tricky' AND target_object = 'dbo.sp_Notify' AND operation = 'EXEC'" "1"
expect "a name with a database keeps it" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object = 'Archive.dbo.upInfo'" "1"
expect "a function call is a CALL" \
  "SELECT COUNT(*) FROM dependencies WHERE source_object = 'dbo.sp_UpdatePrices' AND target_object = 'dbo.fn_Round' AND operation = 'CALL'" "1"
expect "INSERT columns are indexed" \
  "SELECT COUNT(*) FROM column_usage WHERE source_object = 'dbo.sp_UpdatePrices' AND table_name = 'dbo.PriceLog' AND column_name IN ('id', 'old_price') AND operation = 'INSERT'" "2"
expect "UPDATE target columns are indexed" \
  "SELECT COUNT(*) FROM column_usage WHERE source_object = 'dbo.sp_UpdatePrices' AND table_name = 'dbo.upInfo' AND column_name = 'price' AND operation = 'UPDATE'" "1"
expect "CREATE TABLE columns are indexed for completion" \
  "SELECT group_concat(column_name || ' ' || data_type) FROM (SELECT column_name, data_type FROM table_columns WHERE table_name = 'dbo.upInfo' ORDER BY line)" "id INT,price DECIMAL (10, 2)"
expect "explicit view columns are indexed for completion" \
  "SELECT group_concat(column_name) FROM (SELECT column_name FROM table_columns WHERE table_name = 'dbo.v_ActivePrices' ORDER BY line)" "id,price"
expect "simple SELECT columns are resolved through their table alias" \
  "SELECT COUNT(*) FROM column_usage WHERE source_object = 'dbo.sp_Tricky' AND table_name = 'Archive.dbo.upInfo' AND column_name = 'id' AND operation = 'SELECT'" "1"
expect "trigger pseudo-tables are not column usage" \
  "SELECT COUNT(*) FROM column_usage WHERE table_name IN ('dbo.inserted', 'dbo.deleted')" "0"
expect "procedure and function parameters preserve their signature" \
  "SELECT group_concat(name || ' ' || data_type || ' ' || COALESCE(default_value, '-') || ' ' || is_output) FROM (SELECT name, data_type, default_value, is_output FROM parameters WHERE object_name = 'dbo.sp_UpdatePrices' ORDER BY ordinal)" "@minimumPrice DECIMAL (10, 2) 1.00 0,@updatedCount INT - 1"
expect "function parameters are indexed too" \
  "SELECT name || ' ' || data_type FROM parameters WHERE object_name = 'dbo.fn_Round'" "@p DECIMAL (10, 2)"
expect "a trigger depends on its table and not on inserted" \
  "SELECT group_concat(target_object) FROM (SELECT target_object FROM dependencies WHERE source_object = 'dbo.trg_upInfo' ORDER BY line)" \
  "dbo.upInfo,dbo.PriceLog"
expect "objects of every kind are indexed" \
  "SELECT COUNT(*) || ' ' || COUNT(DISTINCT kind) FROM objects" "22 5"
expect "line numbers survive GO and repeat counts" \
  "SELECT line FROM objects WHERE name = 'dbo.trg_upInfo'" "29"
expect "a broken batch is reported with its line" \
  "SELECT source_file || ':' || line FROM parse_errors" "broken.sql:4"
expect "the batch after a broken one is still indexed" \
  "SELECT COUNT(*) FROM objects WHERE name = 'dbo.sp_AfterBroken'" "1"

# ---- views ----
expect "readers and writers of a table" \
  "SELECT (SELECT group_concat(source_object) FROM (SELECT DISTINCT source_object FROM readers WHERE target_object = 'dbo.Totals' ORDER BY 1)) || ' / ' || (SELECT group_concat(source_object) FROM (SELECT DISTINCT source_object FROM writers WHERE target_object = 'dbo.Totals' ORDER BY 1))" \
  "dbo.v_Totals / dbo.sp_C"
expect "calls lists EXEC and function calls" \
  "SELECT COUNT(*) FROM calls WHERE source_object = 'dbo.sp_A'" "1"
expect "an object used but never defined is reported" \
  "SELECT COUNT(*) FROM undefined_objects WHERE name = 'dbo.sp_Notify'" "1"
expect "a defined object is not reported as undefined" \
  "SELECT COUNT(*) FROM undefined_objects WHERE name = 'dbo.upInfo'" "0"
expect "an unreferenced table is unused" \
  "SELECT COUNT(*) FROM unused_objects WHERE name = 'dbo.Orphan'" "1"
expect "a procedure only its own recursion calls is unused" \
  "SELECT COUNT(*) FROM unused_objects WHERE name = 'dbo.sp_Recurse'" "1"
expect "a called procedure is not unused" \
  "SELECT COUNT(*) FROM unused_objects WHERE name = 'dbo.sp_B'" "0"
expect "the most written table has the most writers" \
  "SELECT writers FROM most_written WHERE name = 'dbo.PriceLog'" "3"
expect "and no table has more" \
  "SELECT COUNT(*) FROM most_written WHERE writers > 3" "0"
expect "an object's degree counts what it uses and who uses it" \
  "SELECT depends_on || '/' || used_by FROM object_degree WHERE name = 'dbo.sp_B'" "1/1"
expect "an object defined twice is reported" \
  "SELECT name || ' ' || definitions FROM duplicate_definitions" "dbo.sp_Twice 2"

# ---- impact analysis, call tree and cycles ----
expect "impact: everything a change to a table reaches, nearest first" \
  "SELECT group_concat(affected) FROM (SELECT affected FROM impact WHERE origin = 'dbo.Totals' ORDER BY depth, affected)" \
  "dbo.sp_C,dbo.v_Totals,dbo.sp_B,dbo.sp_Report,dbo.sp_A"
expect "call tree: what a procedure reaches, nearest first" \
  "SELECT group_concat(reached_object) FROM (SELECT reached_object FROM reach WHERE source_object = 'dbo.sp_A' ORDER BY depth)" \
  "dbo.sp_B,dbo.sp_C,dbo.Totals"
expect "the path to a reached object" \
  "SELECT path FROM reach WHERE source_object = 'dbo.sp_A' AND reached_object = 'dbo.Totals'" \
  "dbo.sp_A -EXEC-> dbo.sp_B -EXEC-> dbo.sp_C -UPDATE-> dbo.Totals"
expect "cycles: self recursion and mutual recursion" \
  "SELECT group_concat(object_name || ' ' || depth) FROM (SELECT object_name, depth FROM cycles ORDER BY object_name)" \
  "dbo.sp_PingPong1 2,dbo.sp_PingPong2 2,dbo.sp_Recurse 1"
expect "a cycle does not make an object reach itself" \
  "SELECT COUNT(*) FROM reach WHERE source_object = reached_object" "0"

# ---- encodings ----
expect "a Turkish ANSI (Windows-1254) script without a BOM is read right" \
  "SELECT COUNT(*) FROM objects WHERE name = 'dbo.sp_Fiyat_Güncelle'" "1"
expect "and its dependencies come with it" \
  "SELECT group_concat(target_object || ' ' || operation) FROM dependencies WHERE source_object = 'dbo.sp_Fiyat_Güncelle'" \
  "dbo.upInfo UPDATE"
expect "a UTF-16 script with a BOM is read right" \
  "SELECT group_concat(target_object || ' ' || operation) FROM dependencies WHERE source_object = 'dbo.sp_Utf16'" \
  "dbo.upInfo SELECT"

# ---- incremental indexing ----
work="$(mktemp -d)"
cp -R fixtures "$work/sql"
incremental="$work/incremental.sqlite"

first="$(index "$work/sql" --db "$incremental")"
[[ "$first" == *"(0 reused unchanged)"* ]] || fail "a first run reuses nothing: $first"
[[ "$first" == *"Timing: parse"* ]] || fail "index reports timing phases"

single_worker="$work/single-worker.sqlite"
single_worker_output="$(index fixtures --db "$single_worker" --jobs 1)"
[[ "$single_worker_output" == *"Indexed 8 .sql files"* ]] || fail "--jobs limits indexing workers"

second="$(index "$work/sql" --db "$incremental")"
[[ "$second" == *"(8 reused unchanged)"* ]] || fail "an unchanged folder is reused whole: $second"
expect_on "$incremental" "reused files keep their parse errors" \
  "SELECT source_file || ':' || line FROM parse_errors" "broken.sql:4"

printf 'CREATE PROCEDURE dbo.sp_Added AS BEGIN SELECT 1; END\nGO\n' >> "$work/sql/chain.sql"
third="$(index "$work/sql" --db "$incremental")"
[[ "$third" == *"(7 reused unchanged)"* ]] || fail "only the changed file is read again: $third"
expect_on "$incremental" "a changed file brings in its new object" \
  "SELECT COUNT(*) FROM objects WHERE name = 'dbo.sp_Added'" "1"

rm "$work/sql/dup2.sql"
fourth="$(index "$work/sql" --db "$incremental")"
[[ "$fourth" == *"(7 reused unchanged)"* ]] || fail "a removed file leaves the rest reused: $fourth"
expect_on "$incremental" "a removed file takes its rows with it" \
  "SELECT COUNT(*) FROM duplicate_definitions" "0"

full="$(index "$work/sql" --db "$incremental" --full)"
[[ "$full" == *"(0 reused unchanged)"* ]] || fail "--full reads everything again: $full"

# ---- default schema ----
schema_run="$(index "$work/sql" --db "$incremental" --default-schema app)"
[[ "$schema_run" == *"(0 reused unchanged)"* ]] || fail "a new default schema invalidates the cache: $schema_run"
expect_on "$incremental" "a name without a schema takes the chosen default" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object = 'app.sp_Notify'" "1"
expect_on "$incremental" "a name with a schema keeps it" \
  "SELECT COUNT(*) FROM dependencies WHERE target_object = 'dbo.sp_Notify'" "1"

# ---- command-line queries and index status ----
who_writes="$(dotnet run -c Release --no-build -- who-writes dbo.upInfo --db "$database")"
[[ "$who_writes" == *"dbo.sp_UpdatePrices"* ]] || fail "who-writes lists writers"
short_who_writes="$(dotnet run -c Release --no-build -- who-writes upInfo --db "$database")"
[[ "$short_who_writes" == *"dbo.sp_UpdatePrices"* ]] || fail "unqualified object names use the indexed default schema"
who_updates="$(dotnet run -c Release --no-build -- who-updates dbo.upInfo price --db "$database")"
[[ "$who_updates" == *"dbo.sp_UpdatePrices"* && "$who_updates" == *"UPDATE"* ]] || fail "who-updates lists procedures that update a column"
case_insensitive_writers="$(dotnet run -c Release --no-build -- who-writes DBO.UPINFO --db "$database")"
[[ "$case_insensitive_writers" == *"dbo.sp_UpdatePrices"* ]] || fail "object queries ignore case"
active_home="$work/active-home"
active_index="$active_home/active.sqlite"
mkdir -p "$active_home"
cp "$database" "$active_index"
SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- use "$active_index" >/dev/null
active_writers="$(SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- who-writes dbo.upInfo)"
[[ "$active_writers" == *"dbo.sp_UpdatePrices"* ]] || fail "active index is used outside its source folder"
source_output="$(dotnet run -c Release --no-build -- source sp_UpdatePrices --db "$database")"
[[ "$source_output" == *"prices.sql"* && "$source_output" == *"procedure"* ]] || fail "source finds an object's definition"
objects_output="$(dotnet run -c Release --no-build -- objects --db "$database" --limit 1 --no-color)"
[[ "$objects_output" == *"name"* ]] || fail "objects lists indexed definitions"
find_output="$(dotnet run -c Release --no-build -- find updateprices --db "$database" --no-color)"
[[ "$find_output" == *"dbo.sp_UpdatePrices"* ]] || fail "find locates object names without case sensitivity"
active_prompt="$(SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- prompt)"
[[ "$active_prompt" == "active" ]] || fail "prompt prints the active index name"
prompt_bytes="$(SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- prompt | od -An -tx1 | tr -d ' \n')"
[[ "$prompt_bytes" == "6163746976650a" ]] || fail "prompt ends with a newline"
empty_prompt="$(SQLDEPS_HOME="$work/no-active-index" dotnet run -c Release --no-build -- prompt)"
[[ -z "$empty_prompt" ]] || fail "prompt stays silent without an active index"
doctor_output="$(SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- doctor)"
[[ "$doctor_output" == *"Index is current."* ]] || fail "doctor checks the active index"
indexes_output="$(SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- indexes)"
[[ "$indexes_output" == *"active"* ]] || fail "indexes lists local indexes"
cancel_drop_output="$(printf 'n\n' | SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- drop "$active_index")"
[[ "$cancel_drop_output" == *"Delete index 'active'? [y/N]"* && "$cancel_drop_output" == *"Deletion cancelled."* && -e "$active_index" ]] || fail "drop keeps an index when confirmation is declined"
drop_output="$(printf 'y\n' | SQLDEPS_HOME="$active_home" dotnet run -c Release --no-build -- drop "$active_index")"
[[ "$drop_output" == *"Deleted index 'active'"* && ! -e "$active_index" ]] || fail "drop confirms the deleted index"
limited_writers="$(dotnet run -c Release --no-build -- who-writes dbo.upInfo --db "$database" --limit 1 --table --no-color)"
[[ "$(printf '%s\n' "$limited_writers" | wc -l | tr -d ' ')" == "3" ]] || fail "--limit limits table rows"
who_reads_json="$(dotnet run -c Release --no-build -- who-reads dbo.Totals --db "$database" --json)"
[[ "$who_reads_json" == *'"source_object": "dbo.v_Totals"'* ]] || fail "who-reads emits JSON"
dependencies="$(dotnet run -c Release --no-build -- deps dbo.sp_A --db "$database")"
[[ "$dependencies" == *"dbo.sp_B"* ]] || fail "deps lists direct dependencies"
columns="$(dotnet run -c Release --no-build -- columns dbo.sp_UpdatePrices --db "$database")"
[[ "$columns" == *"dbo.PriceLog"* && "$columns" == *"old_price"* ]] || fail "columns lists resolved column usage"
[[ "$columns" == *"INSERT  dbo.PriceLog"* ]] || fail "columns groups usage by operation and table"
table_columns="$(dotnet run -c Release --no-build -- table-columns upInfo --db "$database" --no-color --no-pager)"
[[ "$table_columns" == *"price"* && "$table_columns" == *"DECIMAL (10, 2)"* ]] || fail "table-columns lists schema columns for completion"
impact_output="$(dotnet run -c Release --no-build -- impact dbo.Totals --db "$database")"
[[ "$impact_output" == *"dbo.sp_Report"* ]] || fail "impact lists downstream objects"
duplicates="$(dotnet run -c Release --no-build -- duplicates --db "$database")"
[[ "$duplicates" == *"dbo.sp_Twice"* ]] || fail "duplicates lists duplicate definitions"
narrow_duplicates="$(COLUMNS=40 dotnet run -c Release --no-build -- duplicates --db "$database" --no-color --no-pager)"
[[ "$narrow_duplicates" == *"  name: dbo.sp_Twice"* && "$narrow_duplicates" == *"  places: dup1.sql:1, dup2.sql:1"* ]] || fail "wide rows switch to a stacked layout"
errors="$(dotnet run -c Release --no-build -- errors --db "$database")"
[[ "$errors" == *"broken.sql"* ]] || fail "errors lists parser failures"
lint_output="$(dotnet run -c Release --no-build -- lint --db "$database" --no-color --no-pager)"
[[ "$lint_output" == *"parse-error"* && "$lint_output" == *"dynamic-sql"* && "$lint_output" == *"duplicate-definition"* && "$lint_output" == *"select-star"* && "$lint_output" == *"update-without-where"* && "$lint_output" == *"merge"* ]] || fail "lint combines index and AST findings"
status_output="$(dotnet run -c Release --no-build -- status "$work/sql" --db "$incremental")"
[[ "$status_output" == *"Index is current."* ]] || fail "status recognises a current index"

printf '\n-- stale --\n' >> "$work/sql/chain.sql"
if dotnet run -c Release --no-build -- status "$work/sql" --db "$incremental" >/dev/null; then
  fail "status exits non-zero when the index is stale"
fi

quiet_index="$(dotnet run -c Release --no-build -- index "$work/sql" --db "$incremental" --quiet)"
[[ -z "$quiet_index" ]] || fail "--quiet suppresses index output"

# ---- language server ----
lsp_initialize='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":null}}'
usage_output="$(dotnet run -c Release --no-build -- 2>&1 || true)"
[[ "$usage_output" == *"sqldeps lsp"* ]] || fail "usage lists the LSP server"
lsp_oversized_output="$(printf 'Content-Length: 16777217\r\n\r\n' | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_oversized_output" == *'exceeds the 16777216 byte limit'* ]] || fail "LSP rejects oversized JSON-RPC bodies"
lsp_invalid_request='{"jsonrpc":"2.0","id":2,"method":"textDocument/hover","params":{}}'
lsp_invalid_request_output="$({ for message in "$lsp_initialize" "$lsp_invalid_request"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp 2>&1 || true)"
[[ "$lsp_invalid_request_output" == *'"id":2,"error":{"code":-32602'* ]] || fail "LSP returns an error for invalid request parameters"
help_output="$(dotnet run -c Release --no-build -- --help)"
[[ "$help_output" == *"sqldeps lsp"* ]] || fail "--help prints usage successfully"
help_command_output="$(dotnet run -c Release --no-build -- help)"
[[ "$help_command_output" == *"sqldeps lsp"* ]] || fail "help prints usage successfully"
version_output="$(dotnet run -c Release --no-build -- --version)"
[[ "$version_output" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || fail "--version prints the assembly version"
lsp_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.demo AS SELECT 1;\nEXEC dbo.demo;"}}}'
lsp_definition='{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10}}}'
lsp_references='{"jsonrpc":"2.0","id":3,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10},"context":{"includeDeclaration":true}}}'
lsp_output="$({ for message in "$lsp_initialize" "$lsp_open" "$lsp_definition" "$lsp_references"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_output" == *'"documentSymbolProvider":true'* ]] || fail "LSP advertises document symbols"
[[ "$lsp_output" == *'"uri":"file:///tmp/sqldeps-lsp-test.sql"'* ]] || fail "LSP resolves definitions in an open document"
[[ "$lsp_output" == *'"positionEncoding":"utf-16"'* && "$lsp_output" == *'"id":3,"result":['* ]] || fail "LSP returns compatible reference locations"
lsp_parse_prefix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-prefix.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.PrefixTable (id INT);\nSELECT FROM;"}}}'
lsp_parse_prefix_symbols='{"jsonrpc":"2.0","id":4,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-prefix.sql"}}}'
lsp_parse_prefix_output="$({ for message in "$lsp_initialize" "$lsp_parse_prefix" "$lsp_parse_prefix_symbols"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_parse_prefix_output" == *'Incorrect syntax'* && "$lsp_parse_prefix_output" == *'"name":"dbo.PrefixTable"'* ]] || fail "LSP preserves valid symbols before a parse error"
lsp_change_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.BeforeChange (id INT);"}}}'
lsp_change='{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql","version":2},"contentChanges":[{"text":"CREATE TABLE dbo.AfterChange (id INT);"}]}}'
lsp_change_symbols='{"jsonrpc":"2.0","id":5,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-rapid-change.sql"}}}'
lsp_change_output="$({ for message in "$lsp_initialize" "$lsp_change_open" "$lsp_change" "$lsp_change_symbols"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_change_output" == *'"name":"dbo.AfterChange"'* && "$lsp_change_output" != *'"name":"dbo.BeforeChange"'* ]] || fail "LSP replaces stale symbols after a rapid document change"
lsp_lint='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lint.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Orders WITH (NOLOCK);\nUPDATE dbo.Orders SET Status = 1;"}}}'
lsp_lint_output="$({ for message in "$lsp_initialize" "$lsp_lint"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_lint_output" == *'"code":"select-star"'* && "$lsp_lint_output" == *'"code":"unfiltered-select"'* && "$lsp_lint_output" == *'"confidence":"low"'* && "$lsp_lint_output" == *'"code":"nolock"'* && "$lsp_lint_output" == *'"code":"update-without-where"'* && "$lsp_lint_output" == *'"severity":2'* && "$lsp_lint_output" == *'"start":{"line":0,"character":7}'* ]] || fail "LSP publishes precise T-SQL lint diagnostics"
lsp_diagnostic='{"jsonrpc":"2.0","id":6,"method":"textDocument/diagnostic","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lint.sql"}}}'
lsp_diagnostic_output="$({ for message in "$lsp_initialize" "$lsp_lint" "$lsp_diagnostic"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_diagnostic_output" == *'"id":6,"result":{"kind":"full"'* && "$lsp_diagnostic_output" == *'"code":"select-star"'* ]] || fail "LSP serves standard document diagnostics"
lsp_code_action='{"jsonrpc":"2.0","id":9,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lint.sql"},"range":{"start":{"line":0,"character":14},"end":{"line":0,"character":20}},"context":{"diagnostics":[{"source":"sqldeps","code":"nolock","range":{"start":{"line":0,"character":14},"end":{"line":0,"character":24}}}]}}}'
lsp_code_action_output="$({ for message in "$lsp_initialize" "$lsp_lint" "$lsp_code_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_code_action_output" == *'"codeActionProvider":true'* && "$lsp_code_action_output" == *'"kind":"quickfix"'* && "$lsp_code_action_output" == *'"newText":"-- sqldeps: disable-next-line=nolock\n"'* ]] || fail "LSP offers a lint suppression quick fix"
lsp_suppressed_lint='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-suppressed-lint.sql","languageId":"sql","version":1,"text":"-- sqldeps: disable-next-line=nolock\nSELECT * FROM dbo.Orders WITH (NOLOCK);"}}}'
lsp_suppressed_lint_output="$({ for message in "$lsp_initialize" "$lsp_suppressed_lint"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_suppressed_lint_output" == *'"code":"select-star"'* && "$lsp_suppressed_lint_output" != *'"code":"nolock"'* ]] || fail "LSP honors next-line lint suppression"
lsp_null_comparison='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-null-comparison.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id <> NULL;"}}}'
lsp_null_comparison_output="$({ for message in "$lsp_initialize" "$lsp_null_comparison"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_null_comparison_output" == *'"code":"null-comparison"'* && "$lsp_null_comparison_output" == *'IS NOT NULL'* ]] || fail "LSP explains NULL comparisons"
lsp_null_comparison_action='{"jsonrpc":"2.0","id":10,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-null-comparison.sql"},"range":{"start":{"line":0,"character":30},"end":{"line":0,"character":40}},"context":{"diagnostics":[{"source":"sqldeps","code":"null-comparison","range":{"start":{"line":0,"character":30},"end":{"line":0,"character":40}}}]}}}'
lsp_null_comparison_action_output="$({ for message in "$lsp_initialize" "$lsp_null_comparison" "$lsp_null_comparison_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_null_comparison_action_output" == *'"title":"Replace with id IS NOT NULL"'* && "$lsp_null_comparison_action_output" == *'"newText":"id IS NOT NULL"'* ]] || fail "LSP offers a NULL comparison quick fix"
lsp_not_in_null='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-not-in-null.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id NOT IN (1, NULL);"}}}'
lsp_not_in_null_output="$({ for message in "$lsp_initialize" "$lsp_not_in_null"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_not_in_null_output" == *'"code":"not-in-null"'* && "$lsp_not_in_null_output" == *'NOT EXISTS'* ]] || fail "LSP warns about NOT IN with NULL"
lsp_paging_literals='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-paging.sql","languageId":"sql","version":1,"text":"SELECT TOP (0) id FROM dbo.Info ORDER BY id OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;"}}}'
lsp_paging_literals_output="$({ for message in "$lsp_initialize" "$lsp_paging_literals"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_paging_literals_output" == *'"code":"top-zero"'* && "$lsp_paging_literals_output" == *'"code":"invalid-fetch"'* ]] || fail "LSP validates constant TOP and FETCH values"
lsp_cte_columns='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-columns.sql","languageId":"sql","version":1,"text":"WITH cte (id, name) AS (SELECT id FROM dbo.Info) SELECT * FROM cte;"}}}'
lsp_cte_columns_output="$({ for message in "$lsp_initialize" "$lsp_cte_columns"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cte_columns_output" == *'"code":"cte-column-count"'* && "$lsp_cte_columns_output" == *'declares 2 column(s) but its query returns 1'* ]] || fail "LSP validates explicit CTE column counts"
lsp_duplicate_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-parameter.sql","languageId":"sql","version":1,"text":"EXEC dbo.DoWork @id = 1, @id = 2;"}}}'
lsp_duplicate_parameter_output="$({ for message in "$lsp_initialize" "$lsp_duplicate_parameter"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_duplicate_parameter_output" == *'"code":"duplicate-procedure-parameter"'* && "$lsp_duplicate_parameter_output" == *'specified more than once'* ]] || fail "LSP validates duplicate procedure parameters"
lsp_count_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-count-column.sql","languageId":"sql","version":1,"text":"SELECT COUNT(optional_value), COUNT(*) FROM dbo.Info;"}}}'
lsp_count_column_output="$({ for message in "$lsp_initialize" "$lsp_count_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_count_column_output" == *'"code":"count-column"'* && "$lsp_count_column_output" == *'excludes NULL values'* ]] || fail "LSP explains COUNT(column) semantics"
lsp_dynamic_concat='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-dynamic-concat.sql","languageId":"sql","version":1,"text":"EXEC(N\u0027SELECT * FROM dbo.Info WHERE id = \u0027 + @id);"}}}'
lsp_dynamic_concat_output="$({ for message in "$lsp_initialize" "$lsp_dynamic_concat"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_dynamic_concat_output" == *'"code":"dynamic-sql-concatenation"'* && "$lsp_dynamic_concat_output" == *'use parameters with sp_executesql'* ]] || fail "LSP warns about dynamic SQL concatenation"
lsp_duplicate_merge_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-merge.sql","languageId":"sql","version":1,"text":"MERGE dbo.Target AS target USING dbo.Source AS source ON target.id = source.id WHEN MATCHED THEN UPDATE SET title = source.title, title = source.other;"}}}'
lsp_duplicate_merge_column_output="$({ for message in "$lsp_initialize" "$lsp_duplicate_merge_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_duplicate_merge_column_output" == *'"code":"duplicate-merge-column"'* && "$lsp_duplicate_merge_column_output" == *'assigned more than once'* ]] || fail "LSP validates duplicate MERGE assignments"
lsp_unknown_procedure_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-procedure-parameter.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id int AS SELECT @id;\nGO\nEXEC dbo.DoWork @other = 1;"}}}'
lsp_unknown_procedure_parameter_output="$({ for message in "$lsp_initialize" "$lsp_unknown_procedure_parameter"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_procedure_parameter_output" == *'"code":"unknown-procedure-parameter"'* && "$lsp_unknown_procedure_parameter_output" == *'has no parameter named'* ]] || fail "LSP validates named procedure arguments from the index"
lsp_unknown_procedure='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-procedure.sql","languageId":"sql","version":1,"text":"EXEC dbo.Missing @id = 1;"}}}'
lsp_unknown_procedure_output="$({ for message in "$lsp_initialize" "$lsp_unknown_procedure"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_procedure_output" == *'"code":"unknown-procedure"'* && "$lsp_unknown_procedure_output" == *'was not found in the workspace index'* ]] || fail "LSP reports unknown qualified procedures"
lsp_unknown_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-function.sql","languageId":"sql","version":1,"text":"SELECT dbo.MissingFn(1);"}}}'
lsp_unknown_function_output="$({ for message in "$lsp_initialize" "$lsp_unknown_function"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_function_output" == *'"code":"unknown-function"'* && "$lsp_unknown_function_output" == *'was not found in the workspace index'* ]] || fail "LSP reports unknown qualified functions"
lsp_excess_procedure_argument='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-excess-procedure-argument.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id int AS SELECT @id;\nGO\nEXEC dbo.DoWork 1, 2;"}}}'
lsp_excess_procedure_argument_output="$({ for message in "$lsp_initialize" "$lsp_excess_procedure_argument"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_excess_procedure_argument_output" == *'"code":"too-many-procedure-arguments"'* && "$lsp_excess_procedure_argument_output" == *'accepts at most 1 argument'* ]] || fail "LSP validates procedure argument count from the index"
lsp_missing_procedure_parameter='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-missing-procedure-parameter.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.DoWork @id int, @name nvarchar(20), @mode int = 1 AS SELECT @id;\nGO\nEXEC dbo.DoWork @id = 1;"}}}'
lsp_missing_procedure_parameter_output="$({ for message in "$lsp_initialize" "$lsp_missing_procedure_parameter"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_missing_procedure_parameter_output" == *'"code":"missing-procedure-parameter"'* && "$lsp_missing_procedure_parameter_output" == *'requires parameter'* ]] || fail "LSP validates required procedure parameters from the index"
lsp_function_argument_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-function-argument-count.sql","languageId":"sql","version":1,"text":"CREATE FUNCTION dbo.AddOne(@value int) RETURNS int AS BEGIN RETURN @value + 1; END\nGO\nSELECT dbo.AddOne(1, 2);"}}}'
lsp_function_argument_count_output="$({ for message in "$lsp_initialize" "$lsp_function_argument_count"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_function_argument_count_output" == *'"code":"function-argument-count"'* && "$lsp_function_argument_count_output" == *'expects 1 argument'* ]] || fail "LSP validates function argument count from the index"
lsp_case_constants='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-case-constants.sql","languageId":"sql","version":1,"text":"SELECT CASE WHEN id = 1 THEN 1 ELSE N\u0027unknown\u0027 END FROM dbo.Info;"}}}'
lsp_case_constants_output="$({ for message in "$lsp_initialize" "$lsp_case_constants"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_case_constants_output" == *'"code":"case-mixed-constants"'* && "$lsp_case_constants_output" == *'mixed constant types'* ]] || fail "LSP explains mixed CASE constant types"
lsp_distinct_order_by='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-distinct-order-by.sql","languageId":"sql","version":1,"text":"SELECT DISTINCT id FROM dbo.Info ORDER BY name;"}}}'
lsp_distinct_order_by_output="$({ for message in "$lsp_initialize" "$lsp_distinct_order_by"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_distinct_order_by_output" == *'"code":"distinct-order-by"'* && "$lsp_distinct_order_by_output" == *'must appear in the SELECT list'* ]] || fail "LSP validates DISTINCT ORDER BY columns"
lsp_leading_wildcard='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-leading-wildcard.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE title LIKE N\u0027%term\u0027;"}}}'
lsp_leading_wildcard_output="$({ for message in "$lsp_initialize" "$lsp_leading_wildcard"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_leading_wildcard_output" == *'"code":"leading-wildcard-like"'* && "$lsp_leading_wildcard_output" == *'prevents an index seek'* ]] || fail "LSP reports leading wildcard LIKE patterns"
lsp_non_sargable_function='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-non-sargable-function.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Products (name NVARCHAR(50)); SELECT * FROM dbo.Products WHERE LOWER(name) = N\u0027x\u0027;"}}}'
lsp_non_sargable_function_output="$({ for message in "$lsp_initialize" "$lsp_non_sargable_function"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_non_sargable_function_output" == *'"code":"non-sargable-function"'* && "$lsp_non_sargable_function_output" == *'prevent an index seek'* ]] || fail "LSP reports function-wrapped predicates"
lsp_not_in_subquery='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-not-in-subquery.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info WHERE id NOT IN (SELECT id FROM dbo.OtherInfo);"}}}'
lsp_not_in_subquery_output="$({ for message in "$lsp_initialize" "$lsp_not_in_subquery"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_not_in_subquery_output" != *'"code":"not-in-subquery"'* ]] || fail "LSP avoids speculative nullable NOT IN warnings"
lsp_cte_alias_shadowing='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-alias-shadowing.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Products (id INT); WITH active AS (SELECT 1 AS id) SELECT p.id FROM dbo.Products active JOIN active p ON p.id = active.id;"}}}'
lsp_cte_alias_shadowing_output="$({ for message in "$lsp_initialize" "$lsp_cte_alias_shadowing"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cte_alias_shadowing_output" == *'"code":"cte-alias-shadowing"'* && "$lsp_cte_alias_shadowing_output" == *'shadows a CTE'* ]] || fail "LSP reports CTE alias shadowing"
lsp_set_operation_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-set-operation-count.sql","languageId":"sql","version":1,"text":"SELECT 1 UNION SELECT 2, 3;"}}}'
lsp_set_operation_count_output="$({ for message in "$lsp_initialize" "$lsp_set_operation_count"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_set_operation_count_output" == *'"code":"set-operation-column-count"'* && "$lsp_set_operation_count_output" == *'same number of columns'* ]] || fail "LSP validates set operation column counts"
lsp_ungrouped_select='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ungrouped-select.sql","languageId":"sql","version":1,"text":"SELECT id, title, COUNT(*) FROM dbo.Info GROUP BY id;"}}}'
lsp_ungrouped_select_output="$({ for message in "$lsp_initialize" "$lsp_ungrouped_select"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_ungrouped_select_output" == *'"code":"ungrouped-select-column"'* && "$lsp_ungrouped_select_output" == *'included in GROUP BY'* ]] || fail "LSP reports ungrouped selected columns"
lsp_prefix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.good AS SELECT 1;\nTHIS IS NOT VALID SQL"}}}'
lsp_prefix_symbols='{"jsonrpc":"2.0","id":6,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql"}}}'
lsp_prefix_output="$({ for message in "$lsp_initialize" "$lsp_prefix" "$lsp_prefix_symbols"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_prefix_output" == *'"name":"dbo.good"'* && "$lsp_prefix_output" == *'Incorrect syntax near'* ]] || fail "LSP retains symbols before a parse error"
lsp_parse_error_batch='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-error-batch.sql","languageId":"sql","version":1,"text":"THIS IS NOT VALID SQL\nGO\nCREATE PROCEDURE dbo.after_error AS SELECT 1;"}}}'
lsp_parse_error_batch_symbols='{"jsonrpc":"2.0","id":7,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-parse-error-batch.sql"}}}'
lsp_parse_error_batch_output="$({ for message in "$lsp_initialize" "$lsp_parse_error_batch" "$lsp_parse_error_batch_symbols"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_parse_error_batch_output" == *'"name":"dbo.after_error"'* && "$lsp_parse_error_batch_output" == *'Incorrect syntax near'* ]] || fail "LSP indexes batches after a parse error"
lsp_syntax='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-syntax.sql","languageId":"sql","version":1,"text":"SELECT FROM dbo.Info;"}}}'
lsp_syntax_output="$({ for message in "$lsp_initialize" "$lsp_syntax"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_syntax_output" == *'Incorrect syntax near'* && "$lsp_syntax_output" == *'"start":{"line":0,"character":7}'* ]] || fail "LSP marks the parser error at its exact column"
lsp_alias_reference='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-alias-reference.sql","languageId":"sql","version":1,"text":"SELECT i. i.prName FROM dbo.Info i;"}}}'
lsp_alias_reference_output="$({ for message in "$lsp_initialize" "$lsp_alias_reference"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_alias_reference_output" == *'"code":"invalid-alias-reference"'* && "$lsp_alias_reference_output" == *'A comma may be missing'* ]] || fail "LSP explains a likely missing comma in an alias reference"
lsp_cross_alias_reference='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cross-alias-reference.sql","languageId":"sql","version":1,"text":"SELECT p. i.prName FROM dbo.Product p JOIN dbo.Info i ON 1 = 1;"}}}'
lsp_cross_alias_reference_output="$({ for message in "$lsp_initialize" "$lsp_cross_alias_reference"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cross_alias_reference_output" == *'"code":"invalid-alias-reference"'* && "$lsp_cross_alias_reference_output" == *'A comma may be missing'* ]] || fail "LSP recognizes a missing comma before another table alias"
lsp_unbound_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unbound-alias.sql","languageId":"sql","version":1,"text":"SELECT x.prName FROM dbo.Info i;"}}}'
lsp_unbound_alias_output="$({ for message in "$lsp_initialize" "$lsp_unbound_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unbound_alias_output" == *'"code":"unbound-identifier"'* && "$lsp_unbound_alias_output" == *'could not be bound'* ]] || fail "LSP reports an unknown table alias"
lsp_derived_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-alias.sql","languageId":"sql","version":1,"text":"SELECT n.pgCode FROM (SELECT p.pgCode FROM dbo.upPage p) n JOIN dbo.upPage_URLDetails d ON n.pgCode = d.pgCode;"}}}'
lsp_derived_alias_output="$({ for message in "$lsp_initialize" "$lsp_derived_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_derived_alias_output" != *'"code":"unbound-identifier"'* ]] || fail "LSP accepts aliases from derived tables"
lsp_table_name_after_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-name-after-alias.sql","languageId":"sql","version":1,"text":"SELECT Info.id FROM dbo.Info i;"}}}'
lsp_table_name_after_alias_output="$({ for message in "$lsp_initialize" "$lsp_table_name_after_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_table_name_after_alias_output" == *'"code":"table-name-after-alias"'* && "$lsp_table_name_after_alias_output" == *'known as alias'* ]] || fail "LSP explains table names cannot be used after an alias"
lsp_alias_code_action='{"jsonrpc":"2.0","id":11,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-name-after-alias.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}},"context":{"diagnostics":[{"source":"sqldeps","code":"table-name-after-alias","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}},"message":"The multi-part identifier Info.id could not be bound: table Info is known as alias \u0027i\u0027 in this query."}]}}}'
lsp_alias_code_action_output="$({ for message in "$lsp_initialize" "$lsp_table_name_after_alias" "$lsp_alias_code_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_alias_code_action_output" == *'"title":"Replace table name with alias \u0027i\u0027"'* && "$lsp_alias_code_action_output" == *'"newText":"i.id"'* ]] || fail "LSP offers an alias replacement quick fix"
lsp_schema_qualified_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema-qualified-alias.sql","languageId":"sql","version":1,"text":"SELECT dbo.Info.id FROM dbo.Info i;"}}}'
lsp_schema_qualified_alias_output="$({ for message in "$lsp_initialize" "$lsp_schema_qualified_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_schema_qualified_alias_output" == *'"code":"table-name-after-alias"'* && "$lsp_schema_qualified_alias_output" == *'known as alias'* ]] || fail "LSP recognizes schema-qualified table names after an alias"
lsp_schema_qualified_alias_action='{"jsonrpc":"2.0","id":12,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema-qualified-alias.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":18}},"context":{"diagnostics":[{"source":"sqldeps","code":"table-name-after-alias","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":18}},"message":"The multi-part identifier dbo.Info.id could not be bound: table dbo.Info is known as alias \u0027i\u0027 in this query."}]}}}'
lsp_schema_qualified_alias_action_output="$({ for message in "$lsp_initialize" "$lsp_schema_qualified_alias" "$lsp_schema_qualified_alias_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_schema_qualified_alias_action_output" == *'"newText":"i.id"'* ]] || fail "LSP replaces a schema-qualified table name with its alias"
lsp_duplicate_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-alias.sql","languageId":"sql","version":1,"text":"SELECT i.id FROM dbo.Info i JOIN dbo.OtherInfo i ON 1 = 1;"}}}'
lsp_duplicate_alias_output="$({ for message in "$lsp_initialize" "$lsp_duplicate_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_duplicate_alias_output" == *'"code":"duplicate-alias"'* && "$lsp_duplicate_alias_output" == *'Duplicate table alias'* ]] || fail "LSP reports duplicate table aliases"
lsp_schema='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT, title NVARCHAR(50));"}}}'
lsp_scope_chain='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-scope-chain.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT); WITH active AS (SELECT id FROM dbo.Info) SELECT a.id FROM active a CROSS APPLY (SELECT a.id AS id) nested WHERE EXISTS (SELECT 1 WHERE a.id = 1);"}}}'
lsp_scope_chain_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_scope_chain"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_scope_chain_output" != *'"code":"unbound-identifier"'* && "$lsp_scope_chain_output" != *'"code":"unknown-column"'* ]] || fail "LSP preserves nested, CTE and APPLY alias scopes"
lsp_local_columns='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-local-columns.sql","languageId":"sql","version":1,"text":"CREATE TABLE #t (id INT, name NVARCHAR(50)); SELECT q.name FROM (SELECT * FROM #t) q; DECLARE @v TABLE (id INT, value INT); SELECT v.value FROM @v v;"}}}'
lsp_local_columns_output="$({ for message in "$lsp_initialize" "$lsp_local_columns"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_local_columns_output" != *'"code":"unknown-table"'* && "$lsp_local_columns_output" != *'"code":"unknown-column"'* ]] || fail "LSP resolves subquery, table-variable and temporary-table columns"
lsp_local_scope='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-local-scope.sql","languageId":"sql","version":1,"text":"CREATE TABLE #scoped (first_value INT);\nSELECT s.first_value FROM #scoped s;\nDROP TABLE #scoped;\nCREATE TABLE #scoped (second_value INT);\nSELECT s.first_value FROM #scoped s;"}}}'
lsp_local_scope_output="$({ for message in "$lsp_initialize" "$lsp_local_scope"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_local_scope_output" == *'"code":"unknown-column"'* && "$lsp_local_scope_output" == *'first_value'* ]] || fail "LSP does not reuse stale temporary-table columns"
lsp_other_schema='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-other-schema.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.OtherInfo (id INT);"}}}'
lsp_unknown_table='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-table.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Missing;"}}}'
lsp_unknown_table_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_unknown_table"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_table_output" == *'"code":"unknown-table"'* && "$lsp_unknown_table_output" == *'was not found in the workspace index'* ]] || fail "LSP reports unknown indexed tables"
lsp_unknown_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-column.sql","languageId":"sql","version":1,"text":"SELECT i.missing FROM dbo.Info i;"}}}'
lsp_unknown_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_unknown_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_column_output" == *'"code":"unknown-column"'* && "$lsp_unknown_column_output" == *'Invalid column name'* ]] || fail "LSP reports a column missing from the indexed table"
lsp_ambiguous_table='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ambiguous-table.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Products (id INT); CREATE TABLE sales.Products (id INT); SELECT id FROM Products;"}}}'
lsp_ambiguous_table_output="$({ for message in "$lsp_initialize" "$lsp_ambiguous_table"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_ambiguous_table_output" == *'"code":"ambiguous-table"'* && "$lsp_ambiguous_table_output" == *'multiple schemas'* ]] || fail "LSP reports ambiguous unqualified table names"
lsp_similar_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-similar-column.sql","languageId":"sql","version":1,"text":"SELECT i.Titel FROM dbo.Info i;"}}}'
lsp_similar_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_similar_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_similar_column_output" == *"Did you mean \\u0027title\\u0027?"* ]] || fail "LSP suggests a similarly named column"
lsp_similar_column_action='{"jsonrpc":"2.0","id":10,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-similar-column.sql"},"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}},"context":{"diagnostics":[{"source":"sqldeps","code":"unknown-column","message":"Invalid column name '\''titel'\'' on '\''dbo.Info'\''. Did you mean '\''title'\''?","range":{"start":{"line":0,"character":7},"end":{"line":0,"character":14}}}]}}}'
lsp_similar_column_action_output="$({ for message in "$lsp_initialize" "$lsp_similar_column" "$lsp_similar_column_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_similar_column_action_output" == *"Replace with \\u0027i.title\\u0027"* && "$lsp_similar_column_action_output" == *'"newText":"i.title"'* ]] || fail "LSP offers a similar-column replacement"
lsp_unknown_where_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-where-column.sql","languageId":"sql","version":1,"text":"SELECT i.id FROM dbo.Info i WHERE i.missing = 1;"}}}'
lsp_unknown_where_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_unknown_where_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_where_column_output" == *'"code":"unknown-column"'* && "$lsp_unknown_where_column_output" == *'Invalid column name'* ]] || fail "LSP reports a missing column in a WHERE clause"
lsp_unknown_order_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unknown-order-column.sql","languageId":"sql","version":1,"text":"SELECT i.id AS itemId FROM dbo.Info i ORDER BY missing;"}}}'
lsp_unknown_order_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_unknown_order_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unknown_order_column_output" == *'"code":"unknown-column"'* && "$lsp_unknown_order_column_output" == *'Invalid column name'* ]] || fail "LSP reports a missing column in ORDER BY"
lsp_order_alias='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-order-alias.sql","languageId":"sql","version":1,"text":"SELECT i.id AS itemId FROM dbo.Info i ORDER BY itemId;"}}}'
lsp_order_alias_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_order_alias"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_order_alias_output" != *'"code":"unknown-column"'* ]] || fail "LSP accepts select aliases in ORDER BY"
lsp_order_ordinal='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-order-ordinal.sql","languageId":"sql","version":1,"text":"SELECT id, title FROM dbo.Info ORDER BY 3;"}}}'
lsp_order_ordinal_output="$({ for message in "$lsp_initialize" "$lsp_order_ordinal"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_order_ordinal_output" == *'"code":"order-by-ordinal"'* && "$lsp_order_ordinal_output" == *'outside the SELECT list'* ]] || fail "LSP validates ORDER BY ordinal positions"
lsp_table_variable_order='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-variable-order.sql","languageId":"sql","version":1,"text":"DECLARE @FollowedProducts TABLE (prCode INT, prPopularityIndex INT); CREATE TABLE dbo.Info (prCode INT); SELECT fl.prCode FROM @FollowedProducts fl JOIN dbo.Info i ON fl.prCode = i.prCode ORDER BY prPopularityIndex;"}}}'
lsp_table_variable_order_output="$({ for message in "$lsp_initialize" "$lsp_table_variable_order"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_table_variable_order_output" != *'Invalid column name 'prPopularityIndex''* ]] || fail "LSP resolves ORDER BY columns from table variables"
lsp_ambiguous_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ambiguous-column.sql","languageId":"sql","version":1,"text":"SELECT id FROM dbo.Info i JOIN dbo.OtherInfo o ON i.id = o.id;"}}}'
lsp_ambiguous_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_other_schema" "$lsp_ambiguous_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_ambiguous_column_output" == *'"code":"ambiguous-column"'* && "$lsp_ambiguous_column_output" == *'Ambiguous column'* ]] || fail "LSP reports an ambiguous unqualified column"
lsp_invalid_insert='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-insert.sql","languageId":"sql","version":1,"text":"INSERT INTO dbo.Info (id, missing) VALUES (1);"}}}'
lsp_invalid_insert_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_invalid_insert"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_invalid_insert_output" == *'"code":"unknown-insert-column"'* && "$lsp_invalid_insert_output" == *'"code":"insert-value-count"'* ]] || fail "LSP validates INSERT target columns and value counts"
lsp_missing_required_insert='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-missing-required-insert.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Required (id INT NOT NULL, name VARCHAR(10) NULL); INSERT INTO dbo.Required (name) VALUES (\u0027x\u0027);"}}}'
lsp_missing_required_insert_output="$({ for message in "$lsp_initialize" "$lsp_missing_required_insert"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_missing_required_insert_output" == *'"code":"missing-required-insert-column"'* ]] || fail "LSP reports omitted required INSERT columns"
lsp_identity_insert='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-identity-insert.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.IdentityTable (id INT IDENTITY(1,1), name VARCHAR(10)); INSERT INTO dbo.IdentityTable (id, name) VALUES (1, \u0027x\u0027);"}}}'
lsp_identity_insert_output="$({ for message in "$lsp_initialize" "$lsp_identity_insert"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_identity_insert_output" == *'"code":"identity-insert"'* ]] || fail "LSP warns about explicit identity INSERT columns"
lsp_implicit_conversion='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-implicit-conversion.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Numbers (id INT NOT NULL); INSERT INTO dbo.Numbers (id) VALUES (\u0027one\u0027);"}}}'
lsp_implicit_conversion_output="$({ for message in "$lsp_initialize" "$lsp_implicit_conversion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_implicit_conversion_output" == *'"code":"implicit-conversion"'* ]] || fail "LSP warns about string-to-numeric INSERT conversions"
lsp_filter_conversion='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-filter-conversion.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Numbers (id INT); SELECT id FROM dbo.Numbers WHERE id = \u00271\u0027;"}}}'
lsp_filter_conversion_output="$({ for message in "$lsp_initialize" "$lsp_filter_conversion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_filter_conversion_output" == *'"code":"implicit-filter-conversion"'* && "$lsp_filter_conversion_output" == *'"confidence":"low"'* && "$lsp_filter_conversion_output" == *'implicit conversion'* ]] || fail "LSP warns about filter type conversions with confidence"
lsp_filter_conversion_action='{"jsonrpc":"2.0","id":7,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-filter-conversion.sql"},"range":{"start":{"line":0,"character":73},"end":{"line":0,"character":76}},"context":{"diagnostics":[{"source":"sqldeps","code":"implicit-filter-conversion","range":{"start":{"line":0,"character":73},"end":{"line":0,"character":76}},"message":"String literal \u00271\u0027 is compared with \u0027INT\u0027 column \u0027id\u0027 and may require an implicit conversion."}]}}}'
lsp_filter_conversion_action_output="$({ for message in "$lsp_initialize" "$lsp_filter_conversion" "$lsp_filter_conversion_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_filter_conversion_action_output" == *'Cast filter literal to column type'* && "$lsp_filter_conversion_action_output" == *'CAST(\u00271\u0027 AS INT)'* ]] || fail "LSP offers a typed literal quick fix"
lsp_transaction='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-transaction.sql","languageId":"sql","version":1,"text":"BEGIN TRANSACTION; UPDATE dbo.Info SET id = 1; COMMIT TRANSACTION;"}}}'
lsp_transaction_output="$({ for message in "$lsp_initialize" "$lsp_transaction"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_transaction_output" == *'"code":"transaction-without-error-handling"'* ]] || fail "LSP warns about transaction error handling"
lsp_select_star='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-select-star.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT, title VARCHAR(10)); SELECT * FROM dbo.Info;"}}}'
lsp_select_star_action='{"jsonrpc":"2.0","id":8,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-select-star.sql"},"range":{"start":{"line":0,"character":58},"end":{"line":0,"character":59}},"context":{"diagnostics":[{"source":"sqldeps","code":"select-star","range":{"start":{"line":0,"character":58},"end":{"line":0,"character":59}},"message":"SELECT *"}]}}}'
lsp_select_star_output="$({ for message in "$lsp_initialize" "$lsp_select_star" "$lsp_select_star_action"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_select_star_output" == *'Expand SELECT * with indexed columns'* && "$lsp_select_star_output" == *'"newText":"id, title"'* ]] || fail "LSP offers a SELECT star expansion quick fix"
lsp_invalid_update='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-update.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET missing = 1;"}}}'
lsp_where_template='{"jsonrpc":"2.0","id":9,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-update.sql"},"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":33}},"context":{"diagnostics":[{"source":"sqldeps","code":"update-without-where","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":33}},"message":"UPDATE has no WHERE clause"}]}}}'
lsp_where_template_output="$({ for message in "$lsp_initialize" "$lsp_invalid_update" "$lsp_where_template"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_where_template_output" == *'Insert safe WHERE 1 = 0 template'* && "$lsp_where_template_output" == *'"newText":" WHERE 1 = 0"'* ]] || fail "LSP offers a safe WHERE template"
lsp_insert_select_count='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-insert-select-count.sql","languageId":"sql","version":1,"text":"INSERT INTO dbo.Info (id, title) SELECT 1;"}}}'
lsp_insert_select_count_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_insert_select_count"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_insert_select_count_output" == *'"code":"insert-select-count"'* && "$lsp_insert_select_count_output" == *'INSERT lists'* ]] || fail "LSP validates INSERT SELECT column counts"
lsp_duplicate_insert_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-insert-column.sql","languageId":"sql","version":1,"text":"INSERT INTO dbo.Info (id, id) VALUES (1, 2);"}}}'
lsp_duplicate_insert_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_duplicate_insert_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_duplicate_insert_column_output" == *'"code":"duplicate-insert-column"'* && "$lsp_duplicate_insert_column_output" == *'appears more than once'* ]] || fail "LSP reports duplicate INSERT columns"
lsp_invalid_update_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_invalid_update"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_invalid_update_output" == *'"code":"unknown-update-column"'* && "$lsp_invalid_update_output" == *'Invalid UPDATE column'* ]] || fail "LSP validates UPDATE target columns"
lsp_cte_update_target='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-update-target.sql","languageId":"sql","version":1,"text":"WITH c AS (SELECT id FROM dbo.Info) UPDATE c SET id = 2;"}}}'
lsp_cte_update_target_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_cte_update_target"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cte_update_target_output" == *'"code":"dml-cte-target"'* ]] || fail "LSP reports CTE DML targets"
lsp_merge_match='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-merge-match.sql","languageId":"sql","version":1,"text":"MERGE dbo.Target AS t USING dbo.Source AS s ON t.id > s.id WHEN MATCHED THEN UPDATE SET t.id = s.id;"}}}'
lsp_merge_match_output="$({ for message in "$lsp_initialize" "$lsp_merge_match"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_merge_match_output" == *'"code":"merge-nondeterministic-match"'* ]] || fail "LSP reports nondeterministic MERGE matches"
lsp_duplicate_update_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-duplicate-update-column.sql","languageId":"sql","version":1,"text":"UPDATE dbo.Info SET id = 1, id = 2;"}}}'
lsp_duplicate_update_column_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_duplicate_update_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_duplicate_update_column_output" == *'"code":"duplicate-update-column"'* && "$lsp_duplicate_update_column_output" == *'assigned more than once'* ]] || fail "LSP reports duplicate UPDATE assignments"
lsp_invalid_alias_update='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-alias-update.sql","languageId":"sql","version":1,"text":"UPDATE i SET missing = 1 FROM dbo.Info i;"}}}'
lsp_invalid_alias_update_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_invalid_alias_update"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_invalid_alias_update_output" == *'"code":"unknown-update-column"'* && "$lsp_invalid_alias_update_output" == *'Invalid UPDATE column'* ]] || fail "LSP resolves aliased UPDATE targets"
lsp_ambiguous_update_target='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-ambiguous-update-target.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT); CREATE TABLE dbo.OtherInfo (id INT); UPDATE i SET id = 1 FROM dbo.Info i JOIN dbo.OtherInfo i ON i.id = i.id;"}}}'
lsp_ambiguous_update_target_output="$({ for message in "$lsp_initialize" "$lsp_ambiguous_update_target"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_ambiguous_update_target_output" == *'"code":"ambiguous-update-target"'* && "$lsp_ambiguous_update_target_output" == *'matches more than one table alias'* ]] || fail "LSP reports ambiguous UPDATE targets"
lsp_unbound_delete_target='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-unbound-delete-target.sql","languageId":"sql","version":1,"text":"DELETE x FROM dbo.Info i;"}}}'
lsp_unbound_delete_target_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_unbound_delete_target"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_unbound_delete_target_output" == *'"code":"unbound-delete-target"'* && "$lsp_unbound_delete_target_output" == *'could not be bound'* ]] || fail "LSP validates DELETE target aliases"
lsp_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql","languageId":"sql","version":1,"text":"SELECT v. FROM dbo.Info v;"}}}'
lsp_completion='{"jsonrpc":"2.0","id":4,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql"},"position":{"line":0,"character":9}}}'
lsp_completion_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_query" "$lsp_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_completion_output" == *'"label":"id"'* && "$lsp_completion_output" == *'"label":"title"'* ]] || fail "LSP completes columns through a table alias"
lsp_table_variable_completion='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-variable-completion.sql","languageId":"sql","version":1,"text":"DECLARE @FollowedProducts TABLE (prCode INT, prPopularityIndex INT); SELECT fl.pr FROM @FollowedProducts fl;"}}}'
lsp_table_variable_completion_request='{"jsonrpc":"2.0","id":13,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-table-variable-completion.sql"},"position":{"line":0,"character":81}}}'
lsp_table_variable_completion_output="$({ for message in "$lsp_initialize" "$lsp_table_variable_completion" "$lsp_table_variable_completion_request"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_table_variable_completion_output" == *'"label":"prCode"'* && "$lsp_table_variable_completion_output" == *'"label":"prPopularityIndex"'* ]] || fail "LSP completes table variable columns through an alias"
lsp_multiline_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-multiline-query.sql","languageId":"sql","version":1,"text":"SELECT v.\nFROM dbo.Info v;"}}}'
lsp_multiline_completion='{"jsonrpc":"2.0","id":10,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-multiline-query.sql"},"position":{"line":0,"character":9}}}'
lsp_multiline_completion_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_multiline_query" "$lsp_multiline_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_multiline_completion_output" == *'"label":"id"'* && "$lsp_multiline_completion_output" == *'"label":"title"'* ]] || fail "LSP completes aliases declared below a select list"
lsp_cte_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql","languageId":"sql","version":1,"text":"WITH active (id, title) AS (SELECT id, title FROM dbo.Info)\nSELECT active.id FROM active;"}}}'
lsp_cte_completion='{"jsonrpc":"2.0","id":7,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql"},"position":{"line":1,"character":14}}}'
lsp_cte_completion_output="$({ for message in "$lsp_initialize" "$lsp_cte_query" "$lsp_cte_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cte_completion_output" == *'"label":"id"'* && "$lsp_cte_completion_output" == *'"label":"title"'* && "$lsp_cte_completion_output" == *'"detail":"CTE"'* ]] || fail "LSP completes explicit CTE columns"
lsp_invalid_cte_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-cte-column.sql","languageId":"sql","version":1,"text":"WITH active (id, title) AS (SELECT 1, N\u0027title\u0027) SELECT active.missing FROM active;"}}}'
lsp_invalid_cte_column_output="$({ for message in "$lsp_initialize" "$lsp_invalid_cte_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_invalid_cte_column_output" == *'"code":"unknown-column"'* && "$lsp_invalid_cte_column_output" == *'Invalid column name'* ]] || fail "LSP validates CTE columns"
lsp_derived_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql","languageId":"sql","version":1,"text":"SELECT q.id FROM (SELECT id, title FROM dbo.Info) q;"}}}'
lsp_derived_completion='{"jsonrpc":"2.0","id":8,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql"},"position":{"line":0,"character":9}}}'
lsp_derived_completion_output="$({ for message in "$lsp_initialize" "$lsp_derived_query" "$lsp_derived_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_derived_completion_output" == *'"label":"id"'* && "$lsp_derived_completion_output" == *'"label":"title"'* && "$lsp_derived_completion_output" == *'"detail":"derived column"'* ]] || fail "LSP completes derived table columns"
lsp_invalid_derived_column='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-invalid-derived-column.sql","languageId":"sql","version":1,"text":"SELECT q.missing FROM (SELECT 1 AS id) q;"}}}'
lsp_invalid_derived_column_output="$({ for message in "$lsp_initialize" "$lsp_invalid_derived_column"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_invalid_derived_column_output" == *'"code":"unknown-column"'* && "$lsp_invalid_derived_column_output" == *'Invalid column name'* ]] || fail "LSP validates derived table columns"
lsp_object_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql","languageId":"sql","version":1,"text":"EXEC dbo."}}}'
lsp_object_completion='{"jsonrpc":"2.0","id":5,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql"},"position":{"line":0,"character":9}}}'
lsp_object_completion_output="$({ for message in "$lsp_initialize" "$lsp_open" "$lsp_object_query" "$lsp_object_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_object_completion_output" == *'"label":"dbo.demo"'* ]] || fail "LSP completes workspace object names"

echo "sqldeps: ok"
