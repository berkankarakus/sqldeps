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
index_json="$(index fixtures --db "$work/index-json.sqlite" --json)"
[[ "$index_json" == *'"sql_files": 8'* && "$index_json" == *'"reused_files": 0'* && "$index_json" == *'"database": "'* && "$index_json" != *"Indexed "* ]] || fail "index emits stable automation JSON"

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
