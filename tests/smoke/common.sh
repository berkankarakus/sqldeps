fail() { echo "FAIL: $*" >&2; exit 1; }

expect_on() {
  local file="$1" description="$2" sql="$3" expected="$4" actual
  actual="$(sqlite3 "$file" "$sql")"
  [[ "$actual" == "$expected" ]] || fail "$description (expected: $expected, got: $actual)"
}

expect() { expect_on "$database" "$@"; }

index() { dotnet run -c Release --no-build -- index "$@"; }

lsp_run() {
  local message
  {
    for message in "$@"; do
      printf 'Content-Length: %s\r\n\r\n' "${#message}"
      printf '%s' "$message"
    done
  } | dotnet run -c Release --no-build -- lsp
}
