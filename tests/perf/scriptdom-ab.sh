#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

baseline="${SQLDEPS_SCRIPTDOM_BASELINE:-}"
candidate="${SQLDEPS_SCRIPTDOM_CANDIDATE:-}"
corpus="${SQLDEPS_PERF_CORPUS:-local-corpus}"
runs="${SQLDEPS_SCRIPTDOM_RUNS:-3}"

if [[ -z "$baseline" || -z "$candidate" ]]; then
  printf '%s\n' 'Set SQLDEPS_SCRIPTDOM_BASELINE and SQLDEPS_SCRIPTDOM_CANDIDATE to ScriptDom DLL paths.' >&2

  exit 2
fi

if [[ ! -f "$baseline" || ! -f "$candidate" ]]; then
  printf 'ScriptDom assembly not found.\nbaseline=%s\ncandidate=%s\n' "$baseline" "$candidate" >&2

  exit 2
fi

if [[ ! -d "$corpus" ]]; then
  printf 'Performance corpus not found: %s\n' "$corpus" >&2

  exit 2
fi

dotnet build tests/perf/ScriptDomBenchmark.csproj \
  -c Release --disable-build-servers -nologo -v q

benchmark="tests/perf/bin/Release/net10.0/ScriptDomBenchmark.dll"

printf '%-10s %-8s %-14s %-14s %-12s %-12s %-12s\n' \
  'variant' 'run' 'elapsed-ms' 'allocated-bytes' 'files' 'parse-errors' 'tokens'

for variant in baseline candidate; do
  assembly="$baseline"

  if [[ "$variant" == "candidate" ]]; then
    assembly="$candidate"
  fi

  for run in $(seq 1 "$runs"); do
    output="$(dotnet "$benchmark" "$assembly" "$corpus")"
    printf '%s\n' "$output" | python3 -c '
import json
import sys

result = json.load(sys.stdin)
print("{elapsed:10.1f} {allocated:14d} {files:12d} {errors:12d} {tokens:12d}".format(
    elapsed=result["ElapsedMilliseconds"],
    allocated=result["AllocatedBytes"],
    files=result["Files"],
    errors=result["ParseErrors"],
    tokens=result["TokenCount"],
))
' | awk -v variant="$variant" -v run="$run" '{ printf "%-10s %-8s %s\n", variant, run, $0 }'
  done
done

printf '%s\n' 'Run the benchmark executable directly to inspect exception and fragment-type details.'
