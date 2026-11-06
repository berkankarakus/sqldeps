#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
build_root="${SQLDEPS_DBEAVER_BUILD_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-build}"
dependency_dir="${SQLDEPS_DBEAVER_DEPS_DIR:-${TMPDIR:-/tmp}/sqldeps-lsp4j}"
classes_root="$build_root/classes"
javac_executable="$(command -v javac)"
java_executable="$(command -v java)"

[[ -d "$dbeaver_home/plugins" ]] || {
    echo "DBeaver plugin dizini bulunamadı: $dbeaver_home/plugins" >&2
    exit 2
}

"$repo_root/dbeaver-plugin/build-local.sh" >/dev/null

plugin_classpath="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name '*.jar' -print | paste -sd: -)"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT

"$javac_executable" \
    -cp "$classes_root:$plugin_classpath:$dependency_dir/org.eclipse.lsp4j-0.24.0.jar:$dependency_dir/org.eclipse.lsp4j.jsonrpc-0.24.0.jar" \
    -d "$test_root" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/DocumentSynchronizerTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/LspDocumentLifecycleTest.java" \
        "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/DiagnosticManagerTest.java" \
        "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/DiagnosticSelectionTest.java" \
        "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/DiagnosticRangeSelectionTest.java" \
        "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/ui/WorkspaceEditSafetyTest.java"

runtime_classpath="$test_root:$classes_root:$plugin_classpath:$dependency_dir/org.eclipse.lsp4j-0.24.0.jar:$dependency_dir/org.eclipse.lsp4j.jsonrpc-0.24.0.jar"
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.DocumentSynchronizerTest
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.LspDocumentLifecycleTest
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.DiagnosticManagerTest
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.DiagnosticSelectionTest
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.DiagnosticRangeSelectionTest
"$java_executable" -cp "$runtime_classpath" org.sqldeps.dbeaver.ui.WorkspaceEditSafetyTest
