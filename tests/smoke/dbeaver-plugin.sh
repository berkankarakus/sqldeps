#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

plugin_root="$repo_root/dbeaver-plugin"

[[ -f "$plugin_root/META-INF/MANIFEST.MF" ]] || fail "DBeaver plugin manifest exists"
[[ -f "$plugin_root/plugin.xml" ]] || fail "DBeaver plugin descriptor exists"
[[ -f "$plugin_root/build.properties" ]] || fail "DBeaver plugin build properties exist"
[[ -x "$plugin_root/build-local.sh" ]] || fail "local DBeaver build script exists"
[[ -x "$plugin_root/install-local.sh" ]] || fail "local DBeaver install script exists"
grep -Fq 'repo1.maven.org/maven2/org/eclipse/lsp4j' "$plugin_root/build-local.sh" \
  || fail "local build downloads pinned LSP4J dependencies"
grep -Fq 'org.eclipse.equinox.p2.director' "$plugin_root/install-local.sh" \
  || fail "local install uses the DBeaver p2 director"
grep -Fq 'org.eclipse.equinox.p2.publisher.FeaturesAndBundlesPublisher' "$plugin_root/install-local.sh" \
  || fail "local install publishes a p2 repository"
grep -Fq 'plugins/org.sqldeps.dbeaver_0.1.0.qualifier.jar' "$plugin_root/install-local.sh" \
  || fail "local install registers the bundle in the DBeaver installation"
grep -Fq 'org.jkiss.dbeaver.sql.editorAddIns' "$plugin_root/plugin.xml" \
  || fail "DBeaver SQL editor add-in extension point is registered"
grep -Fq 'org.jkiss.dbeaver.ui.editors.sql' "$plugin_root/META-INF/MANIFEST.MF" \
  || fail "DBeaver SQL editor bundle is a required dependency"
grep -Fq 'LSPLauncher.Builder' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "LSP4J stdio launcher is used"
grep -Fq 'process.getErrorStream()' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "LSP stderr is drained separately"
grep -Fq 'textDocument/diagnostic' "$plugin_root/README.md" \
  || fail "manual analysis uses the standard diagnostic request"
grep -Fq 'requestCodeActions' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "code actions use the standard LSP request"
grep -Fq 'executeCommand' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "command actions use the standard workspace execute request"
grep -Fq 'showDiagnostics' "$plugin_root/src/org/sqldeps/dbeaver/ui/DiagnosticController.java" \
  || fail "diagnostic messages have a visible UI path"
grep -Fq 'SQLProblemAnnotation' "$plugin_root/src/org/sqldeps/dbeaver/ui/SqldepsProblemAnnotation.java" \
  || fail "diagnostics use DBeaver-supported annotation types"
grep -Fq 'new SqldepsProblemAnnotation' "$plugin_root/src/org/sqldeps/dbeaver/ui/DiagnosticManager.java" \
  || fail "diagnostic messages reach DBeaver SQL annotation hover"
grep -Fq 'NestedSqlEditorLspBridge.activate(nested)' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorTracker.java" \
  || fail "nested diagnostics start after the SQL editor is activated"
grep -Fq 'void partVisible' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorTracker.java" \
  || fail "nested diagnostics handle lazy SQL editor visibility"
grep -Fq 'editor.reloadSyntaxRules()' "$plugin_root/src/org/sqldeps/dbeaver/ui/DiagnosticController.java" \
  || fail "initial nested diagnostics refresh the DBeaver SQL presentation"
grep -Fq 'Show sqldeps code actions' "$plugin_root/src/org/sqldeps/dbeaver/ui/SqldepsQuickFixProcessor.java" \
  || fail "sqldeps quick fixes return a real completion proposal"
grep -Fq 'isDocumentEditable()' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "workspace edits are blocked for read-only SQL documents"
grep -Fq 'org.eclipse.jdt.ui.edit.text.java.correction.assist.proposals' "$plugin_root/plugin.xml" \
  || fail "DBeaver quick fix command is redirected to sqldeps"
grep -Fq 'class QuickFixCommandHandler' "$plugin_root/src/org/sqldeps/dbeaver/commands/QuickFixCommandHandler.java" \
  || fail "DBeaver quick fix command has a sqldeps handler"
grep -Fq '<quickFixProcessor' "$plugin_root/plugin.xml" \
  || fail "DBeaver annotation hover is connected to sqldeps code actions"
grep -Fq 'invocationContext.getOffset()' "$plugin_root/src/org/sqldeps/dbeaver/ui/SqldepsQuickFixProcessor.java" \
  && grep -Fq 'invocationContext.getLength()' "$plugin_root/src/org/sqldeps/dbeaver/ui/SqldepsQuickFixProcessor.java" \
  || fail "annotation quick fix forwards its exact document range"
grep -Fq 'diagnosticsForRange(range)' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "annotation quick fix limits the LSP context to the clicked diagnostic range"
grep -Fq 'describeAction' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "code action menu entries include diagnostic context"
grep -Fq 'codeActionController = new CodeActionController' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorLspBridge.java" \
  || fail "nested SQL editor annotations can open code actions"
grep -Fq 'Math.max(1, end - start)' "$plugin_root/src/org/sqldeps/dbeaver/ui/DiagnosticManager.java" \
  || fail "diagnostic hover ranges always contain a character"
grep -Fq 'new AnnotationMarkerHandler(text)' "$plugin_root/src/org/sqldeps/dbeaver/ui/SqldepsProblemAnnotation.java" \
  || fail "diagnostics do not share a marker instance"
grep -Fq 'DiagnosticsHandler' "$plugin_root/plugin.xml" \
  || fail "diagnostics command handler is registered"
grep -Fq 'SelectIndexHandler' "$plugin_root/plugin.xml" \
  || fail "manual index selection handler is registered"
grep -Fq 'IndexCatalog.load' "$plugin_root/src/org/sqldeps/dbeaver/ui/IndexSelector.java" \
  || fail "registered sqldeps indexes are offered in the selection dialog"
grep -Fq 'SQLEditorNested' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorTracker.java" \
  || fail "nested DBeaver SQL source editors are tracked"
grep -Fq 'NestedSqlEditorLspBridge' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorTracker.java" \
  || fail "nested DBeaver SQL source editors start an LSP bridge"
grep -Fq 'getAdapter(SQLEditorBase.class)' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorTracker.java" \
  || fail "nested SQL source editors are discovered through DBeaver adapters"
grep -Fq 'IndexSelector.choose' "$plugin_root/src/org/sqldeps/dbeaver/commands/SelectIndexHandler.java" \
  || fail "index selection remains available without an active editor bridge"
grep -Fq 'restartAll' "$plugin_root/src/org/sqldeps/dbeaver/commands/SelectIndexHandler.java" \
  || fail "index selection restarts open nested SQL sessions"
grep -Fq 'new ArrayList<>(ACTIVE_BRIDGES.values())' "$plugin_root/src/org/sqldeps/dbeaver/ui/NestedSqlEditorLspBridge.java" \
  || fail "nested session restart uses a stable bridge snapshot"
grep -Fq 'IndexSelectionState.useAutomaticSelection();' "$plugin_root/src/org/sqldeps/dbeaver/commands/AutomaticIndexHandler.java" \
  || fail "automatic index selection updates global state"
grep -Fq 'NestedSqlEditorLspBridge.restartAll();' "$plugin_root/src/org/sqldeps/dbeaver/commands/AutomaticIndexHandler.java" \
  || fail "automatic index selection restarts open nested SQL sessions"
grep -Fq 'sqldeps diagnostics alındı' "$plugin_root/src/org/sqldeps/dbeaver/ui/EditorLspSession.java" \
  || fail "nested diagnostic lifecycle is logged"
grep -Fq 'org.eclipse.ui.startup' "$plugin_root/plugin.xml" \
  || fail "nested editor tracker starts before a procedure source tab opens"
grep -Fq 'SQLEditorNested' "$plugin_root/src/org/sqldeps/dbeaver/commands/AnalyzeSqlHandler.java" \
  || fail "manual analysis supports nested SQL source editors"
grep -Fq 'automaticIndexSelection' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "DBeaver sends explicit index selection mode"
grep -Fq 'applyWorkspaceEdit' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "workspace edits have a guarded application path"
grep -Fq 'MAX_WORKSPACE_EDIT_COUNT' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "workspace edit count is bounded"
grep -Fq 'MAX_WORKSPACE_EDIT_TEXT_CHARACTERS' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "workspace edit text size is bounded"
grep -Fq 'getDocumentChanges()' "$plugin_root/src/org/sqldeps/dbeaver/ui/CodeActionController.java" \
  || fail "unsupported multi-document workspace edits are rejected"
! rg -n 'MSSQL_|/Users/berkan/Projects/' "$plugin_root" \
  || fail "external SQL corpus paths do not leak into the plugin"

echo "sqldeps: dbeaver plugin structure ok"

