#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

corpus="${SQLDEPS_PERF_CORPUS:-local-corpus}"
cli="${SQLDEPS_PERF_CLI:-bin/Release/net10.0/sqldeps.dll}"
job_values="${SQLDEPS_PERF_JOBS:-1 4 8}"

if [[ ! -f "$cli" ]]; then
  printf 'Release build not found: %s\n' "$cli" >&2

  exit 2
fi

if [[ ! -d "$corpus" ]]; then
  printf 'Performance corpus not found: %s\n' "$corpus" >&2

  exit 2
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/sqldeps-perf.XXXXXX")"
trap 'rm -rf "$work"' EXIT

printf 'corpus=%s\n' "$corpus"
printf 'cli=%s\n' "$cli"
printf '%-12s %-8s %-12s %-12s %-8s %-12s %-12s %-10s %-8s %-8s %-8s %-8s\n' 'mode' 'jobs' 'parse-work' 'collect-work' 'files' 'batch-reuse' 'batch-parse' 'generator' 'calls' 'slices' 'sqlite' 'elapsed'

measure() {
  local mode="$1"
  local jobs="$2"
  local database="$work/${mode}-${jobs}.sqlite"
  local output
  local summary
  local timing
  local work_timing
  local batch_timing
  local parse_work_seconds
  local collect_work_seconds
  local reused_batches
  local parsed_batches
  local reused_files
  local generator_timing
  local generator_seconds
  local generator_calls
  local source_slice_hits
  local parse_seconds
  local reach_seconds
  local sqlite_seconds
  local elapsed_seconds

  if [[ "$mode" == "full" ]]; then
    output="$(LC_ALL=C dotnet "$cli" index "$corpus" --db "$database" --jobs "$jobs" --no-color --full 2>&1)"
  else
    output="$(LC_ALL=C dotnet "$cli" index "$corpus" --db "$database" --jobs "$jobs" --no-color 2>&1)"
  fi
  timing="$(printf '%s\n' "$output" | sed -n 's/^Timing: //p')"
  summary="$(printf '%s\n' "$output" | sed -n 's/^Indexed .* (\([0-9][0-9]*\) reused unchanged).*/\1/p')"
  work_timing="$(printf '%s\n' "$output" | sed -n 's/^Work: ScriptDom //p')"
  batch_timing="$(printf '%s\n' "$output" | sed -n 's/^Batches: //p')"
  generator_timing="$(printf '%s\n' "$output" | sed -n 's/^GenerateScript: //p')"
  elapsed_seconds="$(printf '%s\n' "$output" | sed -n 's/^Elapsed: //p')"

  if [[ ! "$summary" =~ ^[0-9]+$ ]]; then
    printf '%s\n' "$output" >&2

    printf 'Could not parse file reuse output.\n' >&2

    exit 1
  fi

  reused_files="$summary"

  if [[ ! "$timing" =~ parse[[:space:]]+([0-9]+[.,]?[0-9]*)s,[[:space:]]+reach[[:space:]]+([0-9]+[.,]?[0-9]*)s,[[:space:]]+SQLite[[:space:]]+([0-9]+[.,]?[0-9]*)s ]]; then
    printf '%s\n' "$output" >&2

    printf 'Could not parse timing output.\n' >&2

    exit 1
  fi

  parse_seconds="${BASH_REMATCH[1]}s"
  reach_seconds="${BASH_REMATCH[2]}s"
  sqlite_seconds="${BASH_REMATCH[3]}s"

  if [[ ! "$work_timing" =~ parse[[:space:]]+([0-9]+[.,]?[0-9]*)s,[[:space:]]+collect[[:space:]]+([0-9]+[.,]?[0-9]*)s ]]; then
    printf '%s\n' "$output" >&2

    printf 'Could not parse work timing output.\n' >&2

    exit 1
  fi

  parse_work_seconds="${BASH_REMATCH[1]}s"
  collect_work_seconds="${BASH_REMATCH[2]}s"

  if [[ ! "$batch_timing" =~ ([0-9]+)[[:space:]]+reused,[[:space:]]+([0-9]+)[[:space:]]+parsed ]]; then
    printf '%s\n' "$output" >&2

    printf 'Could not parse batch timing output.\n' >&2

    exit 1
  fi

  reused_batches="${BASH_REMATCH[1]}"
  parsed_batches="${BASH_REMATCH[2]}"

  if [[ ! "$generator_timing" =~ ([0-9]+[.,]?[0-9]*)s,[[:space:]]+([0-9]+)[[:space:]]+calls,[[:space:]]+([0-9]+)[[:space:]]+source[[:space:]]+slices ]]; then
    printf '%s\n' "$output" >&2

    printf 'Could not parse GenerateScript timing output.\n' >&2

    exit 1
  fi

  generator_seconds="${BASH_REMATCH[1]}s"
  generator_calls="${BASH_REMATCH[2]}"
  source_slice_hits="${BASH_REMATCH[3]}"

  printf '%-12s %-8s %-12s %-12s %-8s %-12s %-12s %-10s %-8s %-8s %-8s %-8s\n' "$mode" "$jobs" "$parse_work_seconds" "$collect_work_seconds" "$reused_files" "$reused_batches" "$parsed_batches" "$generator_seconds" "$generator_calls" "$source_slice_hits" "$sqlite_seconds" "$elapsed_seconds"
}

for jobs in $job_values; do
  measure full "$jobs"
done

incremental_database="$work/incremental-4.sqlite"
LC_ALL=C dotnet "$cli" index "$corpus" --db "$incremental_database" --jobs 4 --no-color --full --quiet >/dev/null
measure incremental 4
