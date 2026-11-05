#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT

if ! java -version >/dev/null 2>&1 || ! javac -version >/dev/null 2>&1; then
    echo "Java 21 JDK is required for the DBeaver adapter tests." >&2
    exit 2
fi

javac \
    -d "$build_root/classes" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentSnapshot.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentState.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentUri.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/DocumentStateTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/DocumentUriTest.java"

java -cp "$build_root/classes" org.sqldeps.dbeaver.lsp.DocumentStateTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.lsp.DocumentUriTest
