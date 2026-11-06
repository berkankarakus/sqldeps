explain_output="$("$sqldeps_app" explain select-star --no-color --no-pager)"
[[ "$explain_output" == *"Diagnostic: select-star"* && "$explain_output" == *"Severity: info"* && "$explain_output" == *"Suggested direction"* ]] || fail "CLI explains a diagnostic rule"
explain_json="$("$sqldeps_app" explain dynamic-sql-concatenation --json)"
[[ "$explain_json" == *'"rule": "dynamic-sql-concatenation"'* && "$explain_json" == *'"default_severity": "warning"'* && "$explain_json" == *'sp_executesql'* && "$explain_json" == *'"references"'* && "$explain_json" == *'learn.microsoft.com'* ]] || fail "CLI exposes evidence-backed diagnostic explanation JSON"

if "$sqldeps_app" explain not-a-rule >/dev/null 2>&1; then
  fail "CLI accepts an unknown diagnostic rule"
fi

printf '%s\n' 'explain: ok'
