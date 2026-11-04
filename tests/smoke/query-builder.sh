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

join_root="$smoke_work/query-builder-join"
join_database="$smoke_work/query-builder-join.sqlite"
mkdir -p "$join_root"
cat > "$join_root/schema.sql" <<'SQL'
CREATE TABLE dbo.Orders (id INT PRIMARY KEY, customer_id INT, total DECIMAL(10, 2));
CREATE TABLE dbo.Customers (id INT PRIMARY KEY, name NVARCHAR(100));
CREATE TABLE dbo.Countries (id INT PRIMARY KEY, name NVARCHAR(100));
ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers FOREIGN KEY (customer_id) REFERENCES dbo.Customers (id);
SQL
dotnet run -c Release --no-build -- index "$join_root" --db "$join_database" --quiet >/dev/null
query_builder_join_output="$(dotnet run -c Release --no-build -- query-builder Orders --join --db "$join_database" --no-color --no-pager)"
[[ "$query_builder_join_output" == *"Query preview: dbo.Orders (table + join)"* \
  && "$query_builder_join_output" == *"JOIN [dbo].[Customers] AS r ON r.[id] = t.[customer_id]"* \
  && "$query_builder_join_output" == *"r.[name]"* ]] || fail "query builder emits a safe single-FK JOIN preview"

if dotnet run -c Release --no-build -- query-builder upInfo --join --db "$database" >/dev/null 2>&1; then
  fail "query builder accepts a table without a single foreign key for JOIN preview"
fi

cat >> "$join_root/schema.sql" <<'SQL'
CREATE TABLE dbo.AmbiguousOrder (id INT PRIMARY KEY, customer_id INT, country_id INT);
ALTER TABLE dbo.AmbiguousOrder ADD CONSTRAINT FK_AmbiguousOrder_Customer FOREIGN KEY (customer_id) REFERENCES dbo.Customers (id);
ALTER TABLE dbo.AmbiguousOrder ADD CONSTRAINT FK_AmbiguousOrder_Country FOREIGN KEY (country_id) REFERENCES dbo.Countries (id);
SQL
dotnet run -c Release --no-build -- index "$join_root" --db "$join_database" --full --quiet >/dev/null
if dotnet run -c Release --no-build -- query-builder AmbiguousOrder --join --db "$join_database" >/dev/null 2>&1; then
  fail "query builder chooses an ambiguous foreign-key relationship"
fi

cat >> "$join_root/schema.sql" <<'SQL'
CREATE TABLE dbo.CompositeParent (part_a INT, part_b INT, name NVARCHAR(100), PRIMARY KEY (part_a, part_b));
CREATE TABLE dbo.CompositeChild (part_a INT, part_b INT);
ALTER TABLE dbo.CompositeChild ADD CONSTRAINT FK_CompositeChild_Parent FOREIGN KEY (part_a, part_b) REFERENCES dbo.CompositeParent (part_a, part_b);
SQL
dotnet run -c Release --no-build -- index "$join_root" --db "$join_database" --full --quiet >/dev/null
if dotnet run -c Release --no-build -- query-builder CompositeChild --join --db "$join_database" >/dev/null 2>&1; then
  fail "query builder chooses a composite foreign-key relationship"
fi

unsupported_root="$smoke_work/query-builder-unsupported"
unsupported_database="$smoke_work/query-builder-unsupported.sqlite"
mkdir -p "$unsupported_root"
cat > "$unsupported_root/unsupported.sql" <<'SQL'
CREATE TABLE dbo.KnownTable (id INT);
CREATE TABLE #TemporaryTable (id INT);
DECLARE @TableVariable TABLE (id INT);
SQL
cat > "$unsupported_root/parse-error.sql" <<'SQL'
CREATE TABLE dbo.ParsePrefix (id INT);
THIS IS NOT VALID SQL;
SQL
dotnet run -c Release --no-build -- index "$unsupported_root" --db "$unsupported_database" --quiet >/dev/null

if dotnet run -c Release --no-build -- query-builder TemporaryTable --db "$unsupported_database" >/dev/null 2>&1; then
  fail "query builder exposes a temporary table as an indexed object"
fi

if dotnet run -c Release --no-build -- query-builder TableVariable --db "$unsupported_database" >/dev/null 2>&1; then
  fail "query builder exposes a table variable as an indexed object"
fi

if dotnet run -c Release --no-build -- query-builder ParsePrefix --db "$unsupported_database" >/dev/null 2>&1; then
  fail "query builder builds a preview from a parse-failed batch"
fi

metadata_root="$smoke_work/query-builder-metadata"
metadata_database="$smoke_work/query-builder-metadata.sqlite"
mkdir -p "$metadata_root"
printf 'CREATE TABLE dbo.MissingMetadata (id INT);\n' > "$metadata_root/schema.sql"
dotnet run -c Release --no-build -- index "$metadata_root" --db "$metadata_database" --quiet >/dev/null
sqlite3 "$metadata_database" "DELETE FROM table_columns WHERE table_name = 'dbo.MissingMetadata';"
if dotnet run -c Release --no-build -- query-builder MissingMetadata --db "$metadata_database" >/dev/null 2>&1; then
  fail "query builder builds a preview without column metadata"
fi

printf '%s\n' 'query-builder: ok'
