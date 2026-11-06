drift_file="$(find "$smoke_work/sql" -type f -name '*.sql' | sort | head -1)"
[[ -n "$drift_file" ]] || fail "drift smoke fixture has no SQL files"
printf '%s\n' 'SELECT 1;' >> "$drift_file"
printf '%s\n' 'SELECT 2;' > "$smoke_work/sql/drift-added.sql"
removed_file="$(find "$smoke_work/sql" -type f -name '*.sql' ! -path "$drift_file" | sort | head -1)"
[[ -n "$removed_file" ]] || fail "drift smoke fixture has no removable SQL file"
removed_relative="${removed_file#"$smoke_work/sql/"}"
rm "$removed_file"

if drift_output="$("$sqldeps_app" drift "$smoke_work/sql" --db "$smoke_incremental" --json 2>/dev/null)"; then
  fail "drift returns a non-zero status when files differ from the index"
fi

[[ "$drift_output" == *'"state": "added"'* && "$drift_output" == *'"path": "drift-added.sql"'* ]] || fail "drift reports added files"
[[ "$drift_output" == *'"state": "changed"'* && "$drift_output" == *'"state": "removed"'* && "$drift_output" == *"$removed_relative"* ]] || fail "drift reports changed and removed files"

printf '%s\n' 'drift: ok'
