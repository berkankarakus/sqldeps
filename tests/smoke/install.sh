#!/usr/bin/env bash
# Clean-home CLI installation and automation smoke test.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
temp_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/sqldeps-install.XXXXXX")"
smoke_home="$temp_root/home"
sql_root="$temp_root/sql"
database="$temp_root/install.sqlite"
nuget_packages="${NUGET_PACKAGES:-${HOME:-}/.nuget/packages}"
mkdir -p "$smoke_home" "$sql_root"

export HOME="$smoke_home"
export DOTNET_CLI_HOME="$smoke_home"
export SQLDEPS_NO_RESTORE=1
export NUGET_PACKAGES="$nuget_packages"

"$repo_root/install.sh" > "$temp_root/install.log"
installed="$smoke_home/.local/bin/sqldeps"
test -x "$installed"

version_output="$($installed version)"
[[ "$version_output" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || { echo "FAIL: install smoke version" >&2; exit 1; }
"$installed" --help | grep -q 'sqldeps index'

printf 'CREATE TABLE dbo.InstallSmoke (id INT);\nSELECT id FROM dbo.InstallSmoke;\n' > "$sql_root/smoke.sql"
index_output="$($installed index "$sql_root" --db "$database" --json --no-pager)"
[[ "$index_output" == *'"database"'* ]] || { echo "FAIL: install smoke index JSON" >&2; exit 1; }

status_output="$($installed status "$sql_root" --db "$database" --json --no-pager)"
[[ "$status_output" == *'"is_current": true'* ]] || { echo "FAIL: install smoke status JSON" >&2; exit 1; }

message='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":null}}'
printf 'Content-Length: %s\r\n\r\n%s' "${#message}" "$message" \
  | "$installed" lsp \
  | grep -q '"id":1'

printf '%s\n' 'sqldeps: install smoke ok'
