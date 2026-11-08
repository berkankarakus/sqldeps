#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."

corpus="${SQLDEPS_PERF_CORPUS:-local-corpus}"
cli="${SQLDEPS_PERF_CLI:-bin/Release/net10.0/sqldeps.dll}"
jobs="${SQLDEPS_PERF_JOBS:-4}"
output="${SQLDEPS_PERF_PROFILE_DIR:-$(mktemp -d "${TMPDIR:-/tmp}/sqldeps-profile.XXXXXX")}"

if ! command -v dotnet-trace >/dev/null 2>&1; then
  printf 'dotnet-trace bulunamadı. dotnet tool install --global dotnet-trace ile kurun.\n' >&2

  exit 2
fi

if ! command -v dotnet-counters >/dev/null 2>&1; then
  printf 'dotnet-counters bulunamadı. dotnet tool install --global dotnet-counters ile kurun.\n' >&2

  exit 2
fi

if [[ ! -f "$cli" ]]; then
  printf 'Release build bulunamadı: %s\n' "$cli" >&2

  exit 2
fi

if [[ ! -d "$corpus" ]]; then
  printf 'Performance corpus bulunamadı: %s\n' "$corpus" >&2

  exit 2
fi

mkdir -p "$output"

printf 'corpus=%s\n' "$corpus"
printf 'cli=%s\n' "$cli"
printf 'jobs=%s\n' "$jobs"
printf 'output=%s\n' "$output"

dotnet-trace collect \
  --profile dotnet-sampled-thread-time \
  --output "$output/index.nettrace" \
  -- dotnet "$cli" index "$corpus" \
      --db "$output/index.sqlite" \
      --jobs "$jobs" \
      --full \
      --no-color \
      --quiet

dotnet-trace report "$output/index.nettrace" topN \
  --number 50 \
  --inclusive \
  > "$output/topn-inclusive.txt"

dotnet-trace report "$output/index.nettrace" topN \
  --number 50 \
  > "$output/topn-exclusive.txt"

dotnet-counters collect \
  --format csv \
  --output "$output/counters.csv" \
  --refresh-interval 1 \
  --counters System.Runtime \
  -- dotnet "$cli" index "$corpus" \
      --db "$output/counters.sqlite" \
      --jobs "$jobs" \
      --full \
      --no-color \
      --quiet

printf 'Profil tamamlandı: %s\n' "$output"
