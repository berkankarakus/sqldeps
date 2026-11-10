help_output="$("$sqldeps_app" --help)"
[[ "$help_output" == *"sqldeps lsp"* ]] || fail "--help prints usage successfully"
command_help_output="$("$sqldeps_app" lint --help)"
[[ "$command_help_output" == *"--fail-on <severity>"* ]] || fail "command-level --help prints usage successfully"
help_command_output="$("$sqldeps_app" help)"
[[ "$help_command_output" == *"sqldeps lsp"* ]] || fail "help prints usage successfully"
version_output="$("$sqldeps_app" --version)"
[[ "$version_output" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || fail "--version prints the assembly version"
if "$sqldeps_app" unknown-command >/dev/null 2>&1; then
  fail "unknown commands return a usage error"
else
  [[ "$?" == "2" ]] || fail "unknown commands return exit code 2"
fi
if "$sqldeps_app" lint --db "$database" --fail-on critical >/dev/null 2>&1; then
  fail "invalid --fail-on values return a usage error"
else
  [[ "$?" == "2" ]] || fail "invalid --fail-on values return exit code 2"
fi
