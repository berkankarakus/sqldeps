#!/usr/bin/env bash
# Runs the selected indexed-data, CLI, LSP, FP/FN, or unit-test suite.
set -euo pipefail
cd "$(dirname "$0")"

usage() {
  printf '%s\n' \
    'Usage: ./test.sh [all|unit|index|cli|spec|install|lsp|mcp|quick-fix|code-generation|query-builder|fp-fn|report]' \
    '' \
    '  all     Build, index fixtures, and run every smoke suite (default).' \
    '  unit    Run the .NET unit-test project only.' \
    '  index   Build, index fixtures, and run index smoke tests.' \
    '  cli     Build, index fixtures, and run CLI smoke tests.' \
    '  spec    Build, index fixtures, and verify normalized CLI/LSP contract snapshots.' \
    '  install Run the clean-home install and CLI automation smoke test.' \
    '  lsp     Build, index fixtures, and run LSP smoke tests.' \
    '  mcp     Build, index fixtures, and run MCP smoke tests.' \
    '  quick-fix Build, then run the focused LSP quick-fix matrix.' \
    '  code-generation Run structural edit and result-set generation safety tests.' \
    '  query-builder Run safe single-table SELECT preview tests.' \
    '  fp-fn   Build, index fixtures, and run FP/FN smoke plus coverage report.' \
    '  report  Run the FP/FN coverage report without rebuilding.'
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

suite="${1:-all}"

run_unit() {
  dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj --no-restore -v minimal
}

prepare_smoke() {
  database="$(mktemp -d)/deps.sqlite"
  dotnet build -c Release -nologo -v q >/dev/null
  dotnet run -c Release --no-build -- index fixtures --db "$database" >/dev/null
  source tests/smoke/common.sh
  cp -R fixtures "$smoke_work/sql"
  smoke_incremental="$smoke_work/incremental.sqlite"
  dotnet run -c Release --no-build -- index "$smoke_work/sql" --db "$smoke_incremental" >/dev/null
}

run_smoke() {
  local smoke_suite="$1"

  prepare_smoke
  case "$smoke_suite" in
    index)
      source tests/smoke/index.sh
      ;;
    cli)
      source tests/smoke/cli.sh
      ;;
    spec)
      source tests/smoke/spec.sh
      ;;
    lsp)
      source tests/smoke/lsp-protocol.sh
      source tests/smoke/lsp-workspace.sh
      source tests/smoke/lsp.sh
      ;;
    mcp)
      source tests/smoke/mcp.sh
      ;;
    quick-fix)
      source tests/smoke/quick-fix.sh
      ;;
  code-generation)
      source tests/smoke/code-generation.sh
      ;;
    query-builder)
      source tests/smoke/query-builder.sh
      ;;
    fp-fn)
      source tests/smoke/fp-fn.sh
      bash tests/smoke/fp-fn-report.sh >/dev/null
      ;;
    all)
      source tests/smoke/index.sh
      source tests/smoke/cli.sh
      source tests/smoke/spec.sh
      source tests/smoke/lsp-protocol.sh
      source tests/smoke/lsp-workspace.sh
      source tests/smoke/lsp.sh
      source tests/smoke/mcp.sh
      source tests/smoke/quick-fix.sh
      source tests/smoke/code-generation.sh
      source tests/smoke/query-builder.sh
      source tests/smoke/fp-fn.sh
      bash tests/smoke/fp-fn-report.sh >/dev/null
      ;;
    *)
      printf 'Unknown smoke suite: %s\n\n' "$smoke_suite" >&2
      usage >&2
      exit 2
      ;;
  esac
}

case "$suite" in
  install)
    bash tests/smoke/install.sh
    ;;
  all|index|cli|spec|lsp|mcp|quick-fix|code-generation|query-builder|fp-fn)
    run_smoke "$suite"
    if [[ "$suite" == all ]]; then
      printf '%s\n' 'sqldeps: ok'
    else
      printf '%s\n' "sqldeps: $suite ok"
    fi
    ;;
  unit)
    run_unit
    ;;
  report)
    bash tests/smoke/fp-fn-report.sh
    ;;
  -*)
    printf 'Unknown test option: %s\n\n' "$suite" >&2
    usage >&2
    exit 2
    ;;
  *)
    printf 'Unknown test suite: %s\n\n' "$suite" >&2
    usage >&2
    exit 2
    ;;
esac
