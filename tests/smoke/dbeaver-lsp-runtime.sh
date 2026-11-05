#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
build_root="${SQLDEPS_DBEAVER_BUILD_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-build}"
dependency_dir="${SQLDEPS_DBEAVER_DEPS_DIR:-${TMPDIR:-/tmp}/sqldeps-lsp4j}"
classes_root="$build_root/classes"
sqldeps_executable="${SQLDEPS_LSP_EXECUTABLE:-$repo_root/bin/Release/net10.0/sqldeps}"

[[ -d "$dbeaver_home/plugins" ]] || {
    echo "DBeaver plugin dizini bulunamadı: $dbeaver_home/plugins" >&2
    exit 2
}

[[ -x "$sqldeps_executable" ]] || {
    echo "sqldeps executable bulunamadı: $sqldeps_executable" >&2
    exit 2
}

"$repo_root/dbeaver-plugin/build-local.sh" >/dev/null

plugin_classpath="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name '*.jar' -print | paste -sd: -)"
runtime_dependencies="$dependency_dir/org.eclipse.lsp4j-0.24.0.jar:$dependency_dir/org.eclipse.lsp4j.jsonrpc-0.24.0.jar"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT

javac \
    -cp "$classes_root:$plugin_classpath:$runtime_dependencies" \
    -d "$test_root" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/LspProcessManagerTest.java"

runtime_classpath="$test_root:$classes_root:$plugin_classpath:$runtime_dependencies"
java -cp "$runtime_classpath" org.sqldeps.dbeaver.lsp.LspProcessManagerTest "$sqldeps_executable"
