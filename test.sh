#!/usr/bin/env bash
# Runs the selected indexed-data, CLI, LSP, FP/FN, or unit-test suite.
set -euo pipefail
cd "$(dirname "$0")"

usage() {
  printf '%s\n' \
    'Usage: ./test.sh [all|unit|index|cli|spec|install|perf|lsp|mcp|dbeaver|dbeaver-java|dbeaver-runtime|dbeaver-lsp-runtime|dbeaver-baseline|quick-fix|code-generation|query-builder|diagnostic-hover|drift|test-skeleton|explain|fp-fn|report]' \
    '' \
    '  all     Build, index fixtures, and run every smoke suite (default).' \
    '  unit    Run the .NET unit-test project only.' \
    '  index   Build, index fixtures, and run index smoke tests.' \
    '  cli     Build, index fixtures, and run CLI smoke tests.' \
    '  spec    Build, index fixtures, and verify normalized CLI/LSP contract snapshots.' \
    '  install Run the clean-home install and CLI automation smoke test.' \
    '  perf    Measure index parse/reach/SQLite phases on a fixed corpus.' \
    '  lsp     Build, index fixtures, and run LSP smoke tests.' \
    '  mcp     Build, index fixtures, and run MCP smoke tests.' \
    '  dbeaver Validate the independent DBeaver plug-in structure.' \
    '  dbeaver-java Compile and run dependency-free Java adapter tests.' \
    '  dbeaver-runtime Build the local plugin and run Eclipse/JFace adapter tests.' \
    '  dbeaver-lsp-runtime Build the local plugin and test the real sqldeps LSP process.' \
    '  dbeaver-baseline Measure adapter startup and diagnostics for multiple LSP processes.' \
    '  quick-fix Build, then run the focused LSP quick-fix matrix.' \
    '  code-generation Run structural edit and result-set generation safety tests.' \
    '  query-builder Run safe single-table SELECT preview tests.' \
    '  diagnostic-hover Run LSP diagnostic explanation hover tests.' \
    '  drift   Report added, changed, and removed SQL files.' \
    '  test-skeleton Generate safe procedure EXEC previews.' \
    '  explain Explain a supported diagnostic rule.' \
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
      source tests/smoke/diagnostic-hover.sh
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
    diagnostic-hover)
      source tests/smoke/diagnostic-hover.sh
      ;;
    drift)
      source tests/smoke/drift.sh
      ;;
    test-skeleton)
      source tests/smoke/test-skeleton.sh
      ;;
    explain)
      source tests/smoke/explain.sh
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
      bash tests/smoke/dbeaver-plugin.sh
      source tests/smoke/quick-fix.sh
      source tests/smoke/code-generation.sh
      source tests/smoke/query-builder.sh
      source tests/smoke/diagnostic-hover.sh
      source tests/smoke/drift.sh
      source tests/smoke/test-skeleton.sh
      source tests/smoke/explain.sh
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
  perf)
    bash tests/perf/index-baseline.sh
    bash tests/perf/lsp-baseline.sh
    ;;
  dbeaver)
    bash tests/smoke/dbeaver-plugin.sh
    ;;
  dbeaver-java)
    bash tests/smoke/dbeaver-java.sh
    ;;
  dbeaver-runtime)
    bash tests/smoke/dbeaver-runtime.sh
    ;;
  dbeaver-lsp-runtime)
    bash tests/smoke/dbeaver-lsp-runtime.sh
    ;;
  dbeaver-baseline)
    bash tests/perf/dbeaver-lsp-process-baseline.sh
    ;;
  all|index|cli|spec|lsp|mcp|quick-fix|query-builder|code-generation|diagnostic-hover|drift|test-skeleton|explain|fp-fn)
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
