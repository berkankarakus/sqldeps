#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
baseline="$repo_root/tests/smoke/fp-fn-coverage.baseline"
write_baseline=false
if [[ "${1:-}" == "--write-baseline" ]]; then
  write_baseline=true
fi

rules="$(grep -hEo '(Add|AddLint)\("[a-z0-9-]+' \
  "$repo_root"/Indexing/DependencyCollector*.cs \
  "$repo_root"/Lsp/QueryColumnValidator.cs \
  "$repo_root"/Lsp/ProcedureParameterValidator.cs \
  | sed -E 's/.*\("//' | sort -u)"
corpora=(
  "$repo_root/tests/smoke/fp-fn.sh"
  "$repo_root/tests/smoke/lsp.sh"
  "$repo_root/tests/smoke/cli.sh"
  "$repo_root/tests/smoke/index.sh"
)

count_references() {
  local rule="$1"
  local count=0
  local corpus matches
  for corpus in "${corpora[@]}"; do
    matches="$(grep -oF "$rule" "$corpus" 2>/dev/null || true)"
    if [[ -n "$matches" ]]; then
      count=$((count + $(printf '%s\n' "$matches" | wc -l | tr -d ' ')))
    fi
  done
  printf '%s' "$count"
}

has_quick_fix() {
  local rule="$1"
  grep -R -Fq "\"$rule\"" "$repo_root/Lsp/LanguageServer.CodeActions.cs" "$repo_root/Lsp/LanguageServer.cs"
}

baseline_count() {
  local rule="$1"
  sed 's/\\t/ /g' "$baseline" | awk -v rule="$rule" '$1 == rule { print $2; found = 1; exit } END { if (!found) print 0 }'
}

if [[ "$write_baseline" == true ]]; then
  for rule in $rules; do
    printf '%s\t%s\n' "$rule" "$(count_references "$rule")"
  done
  exit 0
fi

if [[ ! -f "$baseline" ]]; then
  echo "FP/FN coverage baseline is missing: $baseline" >&2
  exit 1
fi

json_rows=()
missing=0
regressed=0
for rule in $rules; do
  count="$(count_references "$rule")"
  minimum="$(baseline_count "$rule")"
  if (( count == 0 )); then
    missing=$((missing + 1))
    echo "FP/FN coverage missing fixture reference: $rule" >&2
  elif (( count < minimum )); then
    regressed=$((regressed + 1))
    echo "FP/FN coverage regressed for $rule: $count < $minimum" >&2
  fi

  quick_fix=false
  if has_quick_fix "$rule"; then
    quick_fix=true
  fi
  json_rows+=("{\"rule\":\"$rule\",\"fixture_references\":$count,\"minimum_fixture_references\":$minimum,\"quick_fix\":$quick_fix}")
done

printf '{\n  "rules": [\n'
for index in "${!json_rows[@]}"; do
  [[ "$index" -gt 0 ]] && printf ',\n'
  printf '    %s' "${json_rows[$index]}"
done
printf '\n  ],\n  "missing": %s,\n  "regressed": %s\n}\n' "$missing" "$regressed"

if (( missing > 0 || regressed > 0 )); then
  exit 1
fi
