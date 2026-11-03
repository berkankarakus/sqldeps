query_builder_output="$(dotnet run -c Release --no-build -- query-builder upInfo --db "$database" --no-color --no-pager)"
[[ "$query_builder_output" == *"Query preview: dbo.upInfo (table)"* \
  && "$query_builder_output" == *"t.[id]"* \
  && "$query_builder_output" == *"t.[price]"* \
  && "$query_builder_output" == *"FROM [dbo].[upInfo] AS t;"* ]] || fail "query builder emits a qualified single-table SELECT preview"

query_builder_json="$(dotnet run -c Release --no-build -- query-builder upInfo --db "$database" --json --no-color --no-pager)"
[[ "$query_builder_json" == *'"object_name": "dbo.upInfo"'* \
  && "$query_builder_json" == *'"object_kind": "table"'* \
  && "$query_builder_json" == *'"columns": ['* \
  && "$query_builder_json" == *'"sql": "SELECT\n'* ]] || fail "query builder exposes a stable JSON preview contract"

if dotnet run -c Release --no-build -- query-builder Missing --db "$database" >/dev/null 2>&1; then
  fail "query builder accepts an unknown table"
fi

printf '%s\n' 'query-builder: ok'
