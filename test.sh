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
lsp_open='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.demo AS SELECT 1;\nEXEC dbo.demo;"}}}'
lsp_definition='{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10}}}'
lsp_references='{"jsonrpc":"2.0","id":3,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lsp-test.sql"},"position":{"line":1,"character":10},"context":{"includeDeclaration":true}}}'
lsp_output="$({ for message in "$lsp_initialize" "$lsp_open" "$lsp_definition" "$lsp_references"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_output" == *'"documentSymbolProvider":true'* ]] || fail "LSP advertises document symbols"
[[ "$lsp_output" == *'"uri":"file:///tmp/sqldeps-lsp-test.sql"'* ]] || fail "LSP resolves definitions in an open document"
[[ "$lsp_output" == *'"positionEncoding":"utf-16"'* && "$lsp_output" == *'"id":3,"result":['* ]] || fail "LSP returns compatible reference locations"
lsp_lint='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-lint.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Orders WITH (NOLOCK);\nUPDATE dbo.Orders SET Status = 1;"}}}'
lsp_lint_output="$({ for message in "$lsp_initialize" "$lsp_lint"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_lint_output" == *'"code":"select-star"'* && "$lsp_lint_output" == *'"code":"nolock"'* && "$lsp_lint_output" == *'"code":"update-without-where"'* && "$lsp_lint_output" == *'"severity":2'* ]] || fail "LSP publishes T-SQL lint diagnostics"
lsp_prefix='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql","languageId":"sql","version":1,"text":"CREATE PROCEDURE dbo.good AS SELECT 1;\nTHIS IS NOT VALID SQL"}}}'
lsp_prefix_symbols='{"jsonrpc":"2.0","id":6,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/sqldeps-prefix.sql"}}}'
lsp_prefix_output="$({ for message in "$lsp_initialize" "$lsp_prefix" "$lsp_prefix_symbols"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_prefix_output" == *'"name":"dbo.good"'* && "$lsp_prefix_output" == *'Incorrect syntax near'* ]] || fail "LSP retains symbols before a parse error"
lsp_schema='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-schema.sql","languageId":"sql","version":1,"text":"CREATE TABLE dbo.Info (id INT, title NVARCHAR(50));"}}}'
lsp_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql","languageId":"sql","version":1,"text":"SELECT v. FROM dbo.Info v;"}}}'
lsp_completion='{"jsonrpc":"2.0","id":4,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-query.sql"},"position":{"line":0,"character":9}}}'
lsp_completion_output="$({ for message in "$lsp_initialize" "$lsp_schema" "$lsp_query" "$lsp_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_completion_output" == *'"label":"id"'* && "$lsp_completion_output" == *'"label":"title"'* ]] || fail "LSP completes columns through a table alias"
lsp_cte_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql","languageId":"sql","version":1,"text":"WITH active (id, title) AS (SELECT id, title FROM dbo.Info)\nSELECT active.id FROM active;"}}}'
lsp_cte_completion='{"jsonrpc":"2.0","id":7,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-cte-query.sql"},"position":{"line":1,"character":14}}}'
lsp_cte_completion_output="$({ for message in "$lsp_initialize" "$lsp_cte_query" "$lsp_cte_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_cte_completion_output" == *'"label":"id"'* && "$lsp_cte_completion_output" == *'"label":"title"'* && "$lsp_cte_completion_output" == *'"detail":"CTE"'* ]] || fail "LSP completes explicit CTE columns"
lsp_derived_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql","languageId":"sql","version":1,"text":"SELECT q.id FROM (SELECT id, title FROM dbo.Info) q;"}}}'
lsp_derived_completion='{"jsonrpc":"2.0","id":8,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-derived-query.sql"},"position":{"line":0,"character":9}}}'
lsp_derived_completion_output="$({ for message in "$lsp_initialize" "$lsp_derived_query" "$lsp_derived_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_derived_completion_output" == *'"label":"id"'* && "$lsp_derived_completion_output" == *'"label":"title"'* && "$lsp_derived_completion_output" == *'"detail":"derived column"'* ]] || fail "LSP completes derived table columns"
lsp_object_query='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql","languageId":"sql","version":1,"text":"EXEC dbo."}}}'
lsp_object_completion='{"jsonrpc":"2.0","id":5,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///tmp/sqldeps-object-query.sql"},"position":{"line":0,"character":9}}}'
lsp_object_completion_output="$({ for message in "$lsp_initialize" "$lsp_open" "$lsp_object_query" "$lsp_object_completion"; do printf 'Content-Length: %s\r\n\r\n' "${#message}"; printf '%s' "$message"; done; } | dotnet run -c Release --no-build -- lsp)"
[[ "$lsp_object_completion_output" == *'"label":"dbo.demo"'* ]] || fail "LSP completes workspace object names"

echo "sqldeps: ok"
