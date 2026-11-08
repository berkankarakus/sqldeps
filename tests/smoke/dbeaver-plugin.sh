#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

plugin_root="$repo_root/dbeaver-plugin"
template_root="$repo_root/templates/dbeaver-plugin"
code_action_builder="$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionChoiceBuilder.java"
code_action_dialog="$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionSelectionDialog.java"

[[ -f "$plugin_root/META-INF/MANIFEST.MF" ]] || fail "DBeaver plugin manifest exists"
[[ -f "$plugin_root/plugin.xml" ]] || fail "DBeaver plugin descriptor exists"
[[ -f "$plugin_root/build.properties" ]] || fail "DBeaver plugin build properties exist"
[[ -x "$plugin_root/build-local.sh" ]] || fail "local DBeaver build script exists"
[[ -x "$plugin_root/build-update-site.sh" ]] || fail "p2 update-site build script exists"
[[ -x "$plugin_root/install-local.sh" ]] || fail "local DBeaver install script exists"
[[ -f "$plugin_root/feature/feature.xml" ]] || fail "DBeaver feature metadata exists"
[[ -f "$plugin_root/feature/feature.properties" ]] || fail "DBeaver feature properties exist"
[[ -f "$plugin_root/site/site.xml" ]] || fail "DBeaver update-site metadata exists"
grep -Fq 'SQLDEPS_DBEAVER_MAVEN_BASE_URL' "$plugin_root/build-local.sh" \
  && grep -Fq 'org/eclipse/lsp4j/org.eclipse.lsp4j/0.24.0' "$plugin_root/build-local.sh" \
  || fail "local build downloads pinned LSP4J dependencies from a configurable Maven base"
grep -Fq 'org.eclipse.equinox.p2.director' "$plugin_root/install-local.sh" \
  || fail "local install uses the DBeaver p2 director"
grep -Fq 'org.eclipse.equinox.p2.publisher.FeaturesAndBundlesPublisher' "$plugin_root/install-local.sh" \
  || fail "local install publishes a p2 repository"
grep -Fq 'SQLDEPS_DBEAVER_P2_CONFIGURATION' "$plugin_root/install-local.sh" \
  || fail "local install uses a writable temporary p2 configuration"
grep -Fq 'DBeaver kurulumu yazılabilir değil' "$plugin_root/install-local.sh" \
  || fail "local install explains protected DBeaver application bundles"
grep -Fq 'FeaturesAndBundlesPublisher' "$plugin_root/build-update-site.sh" \
  || fail "update-site build publishes a p2 repository"
grep -Fq 'org.sqldeps.dbeaver.feature' "$plugin_root/feature/feature.xml" \
  || fail "DBeaver feature metadata references the feature id"
grep -Fq 'org.sqldeps.dbeaver.feature' "$plugin_root/site/site.xml" \
  || fail "update-site metadata references the feature id"
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
grep -Fq 'change.setRange(toLspRange(documentChange.range()))' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "document synchronization sends a range-based LSP change"
grep -Fq 'requestCodeActions' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "code actions use the standard LSP request"
grep -Fq 'executeCommand' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "command actions use the standard workspace execute request"
grep -Fq 'showDiagnostics' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "diagnostic messages have a visible UI path"
grep -Fq 'SQLProblemAnnotation' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/SqldepsProblemAnnotation.java" \
  || fail "diagnostics use DBeaver-supported annotation types"
grep -Fq 'new SqldepsProblemAnnotation' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticManager.java" \
  || fail "diagnostic messages reach DBeaver SQL annotation hover"
grep -Fq 'NestedSqlEditorLspBridge.activate(nested)' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorTracker.java" \
  || fail "nested diagnostics start after the SQL editor is activated"
grep -Fq 'void partVisible' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorTracker.java" \
  || fail "nested diagnostics handle lazy SQL editor visibility"
grep -Fq 'editor.reloadSyntaxRules()' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "initial nested diagnostics refresh the DBeaver SQL presentation"
grep -Fq 'markStatus("analyzing...")' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "diagnostic requests expose an analyzing status"
grep -Fq 'String result = DiagnosticResultStatus.describe(diagnostics)' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "diagnostic results expose an up-to-date status"
grep -Fq 'stale index' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticResultStatus.java" \
  || fail "stale index diagnostics expose an actionable status"
grep -Fq 'instanceof Map<?, ?> metadata' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticResultStatus.java" \
  || fail "stale index metadata is read from LSP diagnostic data"
grep -Fq 'Show quick fixes for this sqldeps finding' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/SqldepsQuickFixProcessor.java" \
  || fail "sqldeps quick fixes return a real completion proposal"
grep -Fq 'isDocumentEditable()' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionController.java" \
  || fail "workspace edits are blocked for read-only SQL documents"
grep -Fq 'org.sqldeps.dbeaver.commands.quickFixes' "$plugin_root/plugin.xml" \
  || fail "DBeaver quick fix command is registered by sqldeps"
grep -Fq 'class QuickFixCommandHandler' "$plugin_root/src/org/sqldeps/dbeaver/commands/QuickFixCommandHandler.java" \
  || fail "DBeaver quick fix command has a sqldeps handler"
grep -Fq '<quickFixProcessor' "$plugin_root/plugin.xml" \
  || fail "DBeaver annotation hover is connected to sqldeps code actions"
grep -Fq 'invocationContext.getOffset()' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/SqldepsQuickFixProcessor.java" \
  && grep -Fq 'invocationContext.getLength()' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/SqldepsQuickFixProcessor.java" \
  || fail "annotation quick fix forwards its exact document range"
grep -Fq 'diagnosticsForRange(range)' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionController.java" \
  || fail "annotation quick fix limits the LSP context to the clicked diagnostic range"
grep -Fq 'return List.of(matches.get(0));' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/DiagnosticRangeSelection.java" \
  || fail "annotation quick fix narrows overlapping diagnostics to one finding"
grep -Fq 'describeAction' "$code_action_builder" \
  || fail "code action menu entries include diagnostic context"
grep -Fq 'if (choices.size() == 1)' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionController.java" \
  || fail "single quick fixes skip the redundant selection dialog"
grep -Fq 'FilteredItemsSelectionDialog' "$code_action_dialog" \
  || fail "multiple code actions have a searchable selection dialog"
grep -Fq 'protected ItemsFilter createFilter()' "$code_action_dialog" \
  || fail "code action dialog exposes title filtering"
grep -Fq 'categoryFor(Command command)' "$code_action_builder" \
  || fail "code actions are categorized in the selection dialog"
grep -Fq 'sqldeps.who-writes' "$code_action_builder" \
  || fail "dependency writer actions are available in DBeaver"
grep -Fq 'sqldeps.who-reads' "$code_action_builder" \
  || fail "dependency reader actions are available in DBeaver"
grep -Fq 'sqldeps.impact' "$code_action_builder" \
  || fail "dependency impact actions are available in DBeaver"
grep -Fq 'sqldeps.column-writers' "$code_action_builder" \
  || fail "column writer actions are available in DBeaver"
grep -Fq 'Evidence:' "$code_action_builder" \
  || fail "code action menu entries expose evidence metadata"
grep -Fq 'codeActionController = new CodeActionController' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorLspBridge.java" \
  || fail "nested SQL editor annotations can open code actions"
grep -Fq 'workspaceUri = workspaceUri(editor)' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorLspBridge.java" \
  || fail "nested SQL editors preserve a workspace root when one is available"
grep -Fq 'Math.max(1, end - start)' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticManager.java" \
  || fail "diagnostic hover ranges always contain a character"
grep -Fq 'new AnnotationMarkerHandler(text)' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/SqldepsProblemAnnotation.java" \
  || fail "diagnostics do not share a marker instance"
grep -Fq 'DiagnosticsHandler' "$plugin_root/plugin.xml" \
  || fail "diagnostics command handler is registered"
grep -Fq 'SelectIndexHandler' "$plugin_root/plugin.xml" \
  || fail "manual index selection handler is registered"
grep -Fq 'ReindexHandler' "$plugin_root/plugin.xml" \
  || fail "reindex handler is registered"
grep -Fq 'popup:org.jkiss.dbeaver.ui.editors.sql.SQLEditor.EditorContext?after=sql.additions' "$plugin_root/plugin.xml" \
  || fail "sqldeps commands are discoverable from the SQL editor context menu"
grep -Fq 'org.eclipse.ui.bindings' "$plugin_root/plugin.xml" \
  || fail "sqldeps commands have default keyboard bindings"
grep -Fq 'toolbar:org.jkiss.dbeaver.ui.editors.sql.toolbar.side.top?after=tools' "$plugin_root/plugin.xml" \
  || fail "sqldeps commands are discoverable from the SQL editor toolbar"
grep -Fq 'org.sqldeps.dbeaver.sqlEditorContextMenu' "$plugin_root/plugin.xml" \
  || fail "SQL editor context menu has a dedicated sqldeps submenu"
grep -Fq 'definition id="org.sqldeps.dbeaver.sqlEditor"' "$plugin_root/plugin.xml" \
  && grep -Fq 'with variable="activeEditor"' "$plugin_root/plugin.xml" \
  || fail "sqldeps handlers use DBeaver's active SQL editor context"
grep -Fq 'sqldeps.reindex' "$plugin_root/src/org/sqldeps/dbeaver/editor/EditorLspSession.java" \
  || fail "editor sessions expose the LSP reindex command"
grep -Fq 'markRestarting' "$plugin_root/src/org/sqldeps/dbeaver/editor/EditorLspSession.java" \
  || fail "editor sessions expose restart lifecycle status"
grep -Fq 'select a sqldeps index' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "no-index status points to index selection"
grep -Fq 'run Reindex sqldeps index' "$plugin_root/src/org/sqldeps/dbeaver/editor/diagnostics/DiagnosticController.java" \
  || fail "stale status points to reindex"
grep -Fq 'IndexCatalog.load' "$plugin_root/src/org/sqldeps/dbeaver/editor/index/IndexSelector.java" \
  || fail "registered sqldeps indexes are offered in the selection dialog"
grep -Fq 'SQLEditorNested' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorTracker.java" \
  || fail "nested DBeaver SQL source editors are tracked"
grep -Fq 'NestedSqlEditorLspBridge' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorTracker.java" \
  || fail "nested DBeaver SQL source editors start an LSP bridge"
grep -Fq 'getAdapter(SQLEditorBase.class)' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorTracker.java" \
  || fail "nested SQL source editors are discovered through DBeaver adapters"
grep -Fq 'IndexSelector.choose' "$plugin_root/src/org/sqldeps/dbeaver/commands/SelectIndexHandler.java" \
  || fail "index selection remains available without an active editor bridge"
grep -Fq 'restartAll' "$plugin_root/src/org/sqldeps/dbeaver/commands/SelectIndexHandler.java" \
  || fail "index selection restarts open nested SQL sessions"
grep -Fq 'new ArrayList<>(ACTIVE_BRIDGES.values())' "$plugin_root/src/org/sqldeps/dbeaver/editor/NestedSqlEditorLspBridge.java" \
  || fail "nested session restart uses a stable bridge snapshot"
grep -Fq 'IndexSelectionState.useAutomaticSelection();' "$plugin_root/src/org/sqldeps/dbeaver/commands/AutomaticIndexHandler.java" \
  || fail "automatic index selection updates global state"
grep -Fq 'NestedSqlEditorLspBridge.restartAll();' "$plugin_root/src/org/sqldeps/dbeaver/commands/AutomaticIndexHandler.java" \
  || fail "automatic index selection restarts open nested SQL sessions"
grep -Fq 'sqldeps diagnostics received' "$plugin_root/src/org/sqldeps/dbeaver/editor/EditorLspSession.java" \
  || fail "nested diagnostic lifecycle is logged"
grep -Fq 'org.eclipse.ui.startup' "$plugin_root/plugin.xml" \
  || fail "nested editor tracker starts before a procedure source tab opens"
grep -Fq 'SQLEditorNested' "$plugin_root/src/org/sqldeps/dbeaver/commands/AnalyzeSqlHandler.java" \
  || fail "manual analysis supports nested SQL source editors"
grep -Fq 'automaticIndexSelection' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "DBeaver sends explicit index selection mode"
grep -Fq 'sqldeps.lsp.experimentalSuggestions' "$plugin_root/src/org/sqldeps/dbeaver/lsp/LspProcessManager.java" \
  || fail "experimental LSP suggestions have an explicit opt-in"
grep -Fq 'sqldeps.lsp.experimentalSuggestions=true' "$plugin_root/README.md" \
  || fail "experimental LSP suggestion opt-in is documented"
grep -Fq 'ExperimentalSuggestionsHandler' "$plugin_root/plugin.xml" \
  || fail "experimental suggestions have a DBeaver command handler"
grep -Fq 'RegistryToggleState' "$plugin_root/plugin.xml" \
  || fail "experimental suggestions have a toggle command state"
grep -Fq 'SqlEditorLspAddIn.restartAll();' "$plugin_root/src/org/sqldeps/dbeaver/commands/ExperimentalSuggestionsHandler.java" \
  || fail "experimental suggestions restart SQL editor sessions"
grep -Fq 'ExperimentalSuggestionsState.isEnabled()' "$plugin_root/src/org/sqldeps/dbeaver/editor/EditorLspSession.java" \
  || fail "SQL editor sessions pass the experimental suggestions state"
grep -Fq 'applyWorkspaceEdit' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionController.java" \
  || fail "workspace edits have a guarded application path"
grep -Fq 'resolveExternalConfigurationEdit' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/CodeActionController.java" \
  || fail "repository configuration workspace edits have a guarded application path"
grep -Fq 'MAX_EDIT_COUNT' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/WorkspaceEditSafety.java" \
  || fail "workspace edit count is bounded"
grep -Fq 'MAX_TEXT_CHARACTERS' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/WorkspaceEditSafety.java" \
  || fail "workspace edit text size is bounded"
grep -Fq 'getDocumentChanges()' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/WorkspaceEditSafety.java" \
  || fail "unsupported multi-document workspace edits are rejected"
grep -Fq 'Files.isSymbolicLink' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/WorkspaceEditSafety.java" \
  || fail "external workspace edits reject symbolic-link targets"
grep -Fq 'Show quick fixes for this sqldeps finding' "$plugin_root/src/org/sqldeps/dbeaver/editor/actions/SqldepsQuickFixProcessor.java" \
  || fail "annotation quick fix proposal identifies the selected finding"
! rg -n 'MSSQL_|/Users/berkan/Projects/' "$plugin_root" \
  || fail "external SQL corpus paths do not leak into the plugin"

echo "sqldeps: dbeaver plugin structure ok"

