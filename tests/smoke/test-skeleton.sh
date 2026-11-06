skeleton_root="$smoke_work/test-skeleton"
skeleton_database="$smoke_work/test-skeleton.sqlite"
mkdir -p "$skeleton_root"
cat > "$skeleton_root/procedures.sql" <<'SQL'
CREATE PROCEDURE dbo.RunReport
    @id INT,
    @name NVARCHAR(20) = NULL
AS
SELECT @id AS id, @name AS name;
GO
CREATE PROCEDURE dbo.WithOutput
    @result INT OUTPUT
AS
SELECT @result = 1;
SQL
"$sqldeps_app" index "$skeleton_root" --db "$skeleton_database" --quiet >/dev/null
skeleton_output="$("$sqldeps_app" test-skeleton RunReport --db "$skeleton_database" --no-color --no-pager)"
[[ "$skeleton_output" == *"Test skeleton: dbo.RunReport"* \
  && "$skeleton_output" == *"@id = NULL"* \
  && "$skeleton_output" == *"@name = NULL"* ]] || fail "test skeleton emits named procedure arguments"
skeleton_json="$("$sqldeps_app" test-skeleton RunReport --db "$skeleton_database" --json)"
[[ "$skeleton_json" == *'"object_name": "dbo.RunReport"'* && "$skeleton_json" == *'"parameters": ['* && "$skeleton_json" == *'"sql": "EXEC [dbo].[RunReport]'* ]] || fail "test skeleton exposes a stable JSON preview"

if "$sqldeps_app" test-skeleton WithOutput --db "$skeleton_database" >/dev/null 2>&1; then
  fail "test skeleton generates an unsafe OUTPUT parameter call"
fi

printf '%s\n' 'test-skeleton: ok'
