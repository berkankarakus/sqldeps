query_builder_output="$(dotnet run -c Release --no-build -- query-builder tbl_a --db "$database" --no-color --no-pager)"
[[ "$query_builder_output" == *"Query preview: dbo.tbl_a (table)"* \
  && "$query_builder_output" == *"t.[id]"* \
  && "$query_builder_output" == *"t.[price]"* \
  && "$query_builder_output" == *"FROM [dbo].[tbl_a] AS t;"* ]] || fail "query builder emits a qualified single-table SELECT preview"

query_builder_json="$(dotnet run -c Release --no-build -- query-builder tbl_a --db "$database" --json --no-color --no-pager)"
[[ "$query_builder_json" == *'"object_name": "dbo.tbl_a"'* \
  && "$query_builder_json" == *'"object_kind": "table"'* \
  && "$query_builder_json" == *'"columns": ['* \
  && "$query_builder_json" == *'"sql": "SELECT\n'* ]] || fail "query builder exposes a stable JSON preview contract"

if dotnet run -c Release --no-build -- query-builder Missing --db "$database" >/dev/null 2>&1; then
  fail "query builder accepts an unknown table"
fi

join_root="$smoke_work/query-builder-join"
join_database="$smoke_work/query-builder-join.sqlite"
mkdir -p "$join_root"
cat > "$join_root/schema.sql" <<'SQL'
CREATE TABLE dbo.Orders (id INT PRIMARY KEY, customer_id INT, total DECIMAL(10, 2));
CREATE TABLE dbo.Customers (id INT PRIMARY KEY, name NVARCHAR(100));
ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY (customer_id) REFERENCES dbo.Customers (id);
SQL
dotnet run -c Release --no-build -- index "$join_root" --db "$join_database" --quiet >/dev/null
query_builder_join_output="$(dotnet run -c Release --no-build -- query-builder Orders --join --db "$join_database" --no-color --no-pager)"
[[ "$query_builder_join_output" == *"Query preview: dbo.Orders (table + join)"* \
  && "$query_builder_join_output" == *"JOIN [dbo].[Customers] AS r ON r.[id] = t.[customer_id]"* \
  && "$query_builder_join_output" == *"r.[name]"* ]] || fail "query builder emits a safe single-FK JOIN preview"

if dotnet run -c Release --no-build -- query-builder tbl_a --join --db "$database" >/dev/null 2>&1; then
  fail "query builder accepts a table without a single foreign key for JOIN preview"
fi

printf '%s\n' 'query-builder: ok'
