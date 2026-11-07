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
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentPosition.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentRange.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentChange.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentState.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/lsp/DocumentUri.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/index/IndexCatalog.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/index/IndexMetadata.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/index/IndexStatus.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/index/IndexSelectionState.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/DocumentChangeDebouncer.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/DiagnosticRequestGate.java" \
    "$repo_root/dbeaver-plugin/src/org/sqldeps/dbeaver/editor/actions/ExperimentalSuggestionsState.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/DocumentStateTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/lsp/DocumentUriTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/index/IndexCatalogTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/index/IndexStatusTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/index/IndexSelectionStateTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/DocumentChangeDebouncerTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/DiagnosticRequestGateTest.java" \
    "$repo_root/dbeaver-plugin/test/org/sqldeps/dbeaver/editor/actions/ExperimentalSuggestionsStateTest.java"

java -cp "$build_root/classes" org.sqldeps.dbeaver.lsp.DocumentStateTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.lsp.DocumentUriTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.index.IndexCatalogTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.index.IndexStatusTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.index.IndexSelectionStateTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.DocumentChangeDebouncerTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.DiagnosticRequestGateTest
java -cp "$build_root/classes" org.sqldeps.dbeaver.editor.actions.ExperimentalSuggestionsStateTest
