#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

cli="${SQLDEPS_PERF_CLI:-bin/Release/net10.0/sqldeps.dll}"
root="${SQLDEPS_LSP_PERF_ROOT:-fixtures}"
requests="${SQLDEPS_LSP_PERF_REQUESTS:-100}"

if [[ ! -f "$cli" ]]; then
  printf 'Release build not found: %s\n' "$cli" >&2

  exit 2
fi

if [[ ! -d "$root" ]]; then
  printf 'LSP performance root not found: %s\n' "$root" >&2

  exit 2
fi

root="$(cd "$root" && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/sqldeps-lsp-perf.XXXXXX")"
trap 'rm -rf "$work"' EXIT

root_uri="file://$root"
document_uri="$root_uri/.sqldeps-lsp-perf.sql"
messages="$work/messages.jsonrpc"

lsp_frame() {
  local message="$1"
  local byte_length
  byte_length="$(printf '%s' "$message" | wc -c | tr -d '[:space:]')"
  printf 'Content-Length: %s\r\n\r\n%s' "$byte_length" "$message" >> "$messages"
}

lsp_frame "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"rootUri\":\"$root_uri\"}}"
lsp_frame "{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{\"textDocument\":{\"uri\":\"$document_uri\",\"languageId\":\"sql\",\"version\":1,\"text\":\"SELECT * FROM dbo.Products;\"}}}"

for ((index = 0; index < requests; index++)); do
  lsp_frame "{\"jsonrpc\":\"2.0\",\"id\":$((index + 10)),\"method\":\"workspace/symbol\",\"params\":{\"query\":\"Product\"}}"
  lsp_frame "{\"jsonrpc\":\"2.0\",\"id\":$((index + 1000)),\"method\":\"textDocument/completion\",\"params\":{\"textDocument\":{\"uri\":\"$document_uri\"},\"position\":{\"line\":0,\"character\":10}}}"
done

printf 'requests=%s\n' "$((requests * 2))"
/usr/bin/time -p dotnet "$cli" lsp < "$messages" > /dev/null
