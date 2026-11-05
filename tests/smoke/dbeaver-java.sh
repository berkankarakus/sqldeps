#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
build_root="$(mktemp -d)"
trap 'rm -rf "$build_root"' EXIT

javac \
    -d "$build_root/classes" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentSnapshot.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentState.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/DocumentStateTest.java"

java -cp "$build_root/classes" org.sqldeps.dbeaver.lsp.DocumentStateTest
