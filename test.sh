#!/usr/bin/env bash
# Runs the selected indexed-data, CLI, LSP, FP/FN, or unit-test suite.
set -euo pipefail
cd "$(dirname "$0")"

# Personal build settings (for example a locally packed ScriptDom); see .sqldeps-local.env.example.
if [[ -f .sqldeps-local.env ]]; then
  # shellcheck disable=SC1091
  source .sqldeps-local.env
fi
sqldeps_app="$(pwd)/bin/Release/net10.0/sqldeps"

if [[ -z "${DOTNET_CLI_HOME:-}" ]]; then
  export DOTNET_CLI_HOME="${TMPDIR:-/tmp}/sqldeps-dotnet-home"
fi

mkdir -p "$DOTNET_CLI_HOME"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export MSBuildEnableWorkloadResolver=false
export NuGetAudit="${NuGetAudit:-false}"

usage() {
  printf '%s\n' \
    'Usage: ./test.sh [suite] [--no-build]' \
    '  suite   all, unit, index, cli, spec, install, perf, lsp, mcp, dbeaver, dbeaver-java, dbeaver-runtime,' \
    '          dbeaver-lsp-runtime, dbeaver-baseline, quick-fix, code-generation, query-builder, diagnostic-hover,' \
    '          drift, test-skeleton, explain, fp-fn, or report' \
    '  --no-build  reuse existing build outputs for smoke and unit suites' \
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
skip_build=false

if [[ "${2:-}" == "--no-build" ]]; then
  skip_build=true
fi

if [[ -n "${2:-}" && "${2}" != "--no-build" ]] || [[ -n "${3:-}" ]]; then
  usage >&2
  exit 2
fi

run_unit() {
  dotnet build-server shutdown >/dev/null 2>&1 || true

  if [[ "$skip_build" == false ]]; then
    ensure_dotnet_restore
  fi

  if uses_native_mtp; then
    local test_project="tests/sqldeps.Tests/sqldeps.Tests.csproj"
    local test_dll="tests/sqldeps.Tests/bin/Debug/net10.0/sqldeps.Tests.dll"

    if [[ "$skip_build" == true ]]; then
      :
    else
      dotnet build "$test_project" -c Debug --no-restore --disable-build-servers \
        -p:UseSharedCompilation=false -p:BuildInParallel=false -nologo -v minimal
    fi

    dotnet "$test_dll" --progress off --no-ansi

    return
  fi

  if [[ "$skip_build" == true ]]; then
      dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj \
        --no-build --no-restore --disable-build-servers -m:1 -v minimal
  else
      dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj \
        --no-restore --disable-build-servers -m:1 -v minimal
  fi
}

uses_native_mtp() {
  [[ -f global.json ]] && grep -Eq '"runner"[[:space:]]*:[[:space:]]*"Microsoft\.Testing\.Platform"' global.json
}

ensure_dotnet_restore() {
  local project
  local project_directory
  local assets

  for project in SqlDeps.csproj Core/SqlDeps.Core.csproj tests/sqldeps.Tests/sqldeps.Tests.csproj; do
    project_directory="${project%/*}"
    if [[ "$project_directory" == "$project" ]]; then
      project_directory="."
    fi

    assets="$project_directory/obj/project.assets.json"

    # Restore again when the requested ScriptDom version differs from the one the assets were restored with.
    expected_scriptdom="Microsoft.SqlServer.TransactSql.ScriptDom/${ScriptDomVersion:-170.191.0}\""
    if [[ ! -f "$assets" || "$project" -nt "$assets" ]] || ! grep -Fq "$expected_scriptdom" "$assets"; then
      dotnet restore "$project" --disable-parallel --ignore-failed-sources
    fi
  done
}

smoke_suite_uses_lsp() {
  case "$1" in
    all|code-generation|diagnostic-hover|fp-fn|lsp|quick-fix|spec)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

smoke_suite_uses_incremental_index() {
  case "$1" in
    all|cli|drift|index)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

prepare_smoke() {
  local smoke_suite="$1"

  dotnet build-server shutdown >/dev/null 2>&1 || true

  database="$(mktemp -d)/deps.sqlite"
  if [[ "$skip_build" == false ]]; then
    ensure_dotnet_restore
    dotnet build -c Release --no-restore --disable-build-servers \
      -p:UseSharedCompilation=false -p:BuildInParallel=false -nologo -v q >/dev/null
  fi
  "$sqldeps_app" index fixtures --db "$database" >/dev/null
  source tests/smoke/common.sh

  if smoke_suite_uses_lsp "$smoke_suite"; then
    lsp_session_start
  fi

  if smoke_suite_uses_incremental_index "$smoke_suite"; then
    cp -R fixtures "$smoke_work/sql"
    smoke_incremental="$smoke_work/incremental.sqlite"
    "$sqldeps_app" index "$smoke_work/sql" --db "$smoke_incremental" >/dev/null
  fi
}

run_smoke() {
  local smoke_suite="$1"

  prepare_smoke "$smoke_suite"
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
      source tests/smoke/lsp-cli.sh
      source tests/smoke/lsp-protocol.sh
      source tests/smoke/lsp-workspace.sh
      source tests/smoke/lsp-diagnostics-actions.sh
      source tests/smoke/lsp.sh
      source tests/smoke/lsp-schema-analysis.sh
      source tests/smoke/lsp-insert-exec.sh
      source tests/smoke/lsp-index-diagnostics.sh
      source tests/smoke/lsp-neovim.sh
      source tests/smoke/lsp-language-features.sh
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
      source tests/smoke/lsp-cli.sh
      source tests/smoke/lsp-protocol.sh
      source tests/smoke/lsp-workspace.sh
      source tests/smoke/lsp-diagnostics-actions.sh
      source tests/smoke/lsp.sh
      source tests/smoke/lsp-schema-analysis.sh
      source tests/smoke/lsp-insert-exec.sh
      source tests/smoke/lsp-index-diagnostics.sh
      source tests/smoke/lsp-neovim.sh
      source tests/smoke/lsp-language-features.sh
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

started_at=$SECONDS

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

printf 'Elapsed: %ss (%s)\n' "$((SECONDS - started_at))" "$suite"
