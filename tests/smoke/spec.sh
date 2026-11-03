#!/usr/bin/env bash
set -euo pipefail

command -v jq >/dev/null 2>&1 || fail "spec suite requires jq"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cli_snapshot="$repo_root/tests/smoke/specs/cli-lint.json"
cli_actual="$(dotnet run -c Release --no-build -- lint --db "$database" --json --no-color --no-pager --limit 5 | jq -cS 'map({rule,severity,source,confidence,stale}) | sort_by(.rule)')"
diff -u "$cli_snapshot" <(printf '%s\n' "$cli_actual") || fail "CLI lint contract snapshot changed"

lsp_spec='{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/sqldeps-spec.sql","languageId":"sql","version":1,"text":"SELECT * FROM dbo.Orders WITH (NOLOCK);"}}}'
lsp_output="$(lsp_run "$lsp_initialize" "$lsp_spec")"
lsp_actual="$(printf '%s' "$lsp_output" | tr -d '\r' | sed -E 's/Content-Length: [0-9]+//g' | grep '^{' | jq -s -cS '[.[] | select(.method == "textDocument/publishDiagnostics") | .params.diagnostics[] | {code,severity,range,source,data}] | sort_by(.code, .range.start.character)')"
diff -u "$repo_root/tests/smoke/specs/lsp-diagnostics.json" <(printf '%s\n' "$lsp_actual") || fail "LSP diagnostics contract snapshot changed"
