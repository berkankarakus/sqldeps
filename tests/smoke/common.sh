fail() { echo "FAIL: $*" >&2; exit 1; }

smoke_work="$(mktemp -d)"

expect_on() {
  local file="$1" description="$2" sql="$3" expected="$4" actual
  actual="$(sqlite3 "$file" "$sql")"
  [[ "$actual" == "$expected" ]] || fail "$description (expected: $expected, got: $actual)"
}

expect() { expect_on "$database" "$@"; }

index() { dotnet run -c Release --no-build -- index "$@"; }

lsp_initialize='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":null}}'

lsp_frame() {
  local message="$1"
  local byte_length
  byte_length="$(printf '%s' "$message" | wc -c | tr -d '[:space:]')"
  printf 'Content-Length: %s\r\n\r\n%s' "$byte_length" "$message"
}

lsp_run() {
  local message
  {
    for message in "$@"; do
      lsp_frame "$message"
    done
  } | dotnet run -c Release --no-build -- lsp
}
