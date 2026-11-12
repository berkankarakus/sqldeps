/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package org.sqldeps.dbeaver.editor;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ScheduledExecutorService;

import org.eclipse.core.runtime.IStatus;
import org.eclipse.core.runtime.Status;
import org.eclipse.jface.text.IDocument;
import org.eclipse.swt.widgets.Display;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;
import org.sqldeps.dbeaver.Activator;
import org.sqldeps.dbeaver.editor.index.IndexSelectionState;
import org.sqldeps.dbeaver.editor.index.IndexSelector;

/** Connects a DBeaver nested source editor to the shared sqldeps LSP service. */
public final class NestedSqlEditorLspBridge implements AutoCloseable {
    private static final NestedEditorBridgeRegistry ACTIVE_BRIDGES = new NestedEditorBridgeRegistry();

    private final SQLEditorNested<?> editor;
    private final ScheduledExecutorService scheduler = SharedLspScheduler.instance();
    private final String documentUri;
    private final String workspaceUri;

    private IDocument document;
    private EditorLspSession session;
    private boolean startScheduled;
    private boolean closed;

    private NestedSqlEditorLspBridge(SQLEditorNested<?> editor) {
        this.editor = editor;
        documentUri = EditorDocumentContext.documentUri(editor, "nested-");
        workspaceUri = EditorDocumentContext.workspaceUri(editor);
        NestedEditorContextMenu.install(editor);
    }

    /** Registers a nested editor without starting a process until it becomes active. */
    public static void attach(SQLEditorNested<?> editor) {
        synchronized (ACTIVE_BRIDGES) {
            NestedSqlEditorLspBridge existing = ACTIVE_BRIDGES.forEditor(editor);
            if (existing != null) {
                ACTIVE_BRIDGES.register(editor, existing);
                return;
            }

            NestedSqlEditorLspBridge bridge = new NestedSqlEditorLspBridge(editor);
            ACTIVE_BRIDGES.register(editor, bridge);
        }
    }

    /** Returns the bridge for the most recently active nested editor. */
    public static NestedSqlEditorLspBridge active() {
        return ACTIVE_BRIDGES.active();
    }

    /** Clears the active nested-editor pointer when DBeaver leaves the source tab. */
    public static void clearActive() {
        ACTIVE_BRIDGES.clearActive();
    }

    /** Removes and closes the bridge owned by a nested editor. */
    public static void detach(SQLEditorNested<?> editor) {
        NestedSqlEditorLspBridge bridge;
        synchronized (ACTIVE_BRIDGES) {
            bridge = ACTIVE_BRIDGES.remove(editor);
        }

        if (bridge != null) {
            if (ACTIVE_BRIDGES.isActive(bridge)) {
                ACTIVE_BRIDGES.clearActive();
            }

            bridge.close();
        }
    }

    /** Returns the bridge registered for a nested editor, if any. */
    public static NestedSqlEditorLspBridge forEditor(SQLEditorNested<?> editor) {
        return ACTIVE_BRIDGES.forEditor(editor);
    }

    /** Starts analysis after a nested editor has a document and becomes visible. */
    public static void activate(SQLEditorNested<?> editor) {
        NestedSqlEditorLspBridge bridge = forEditor(editor);
        if (bridge != null) {
            bridge.activate();
        }
    }

    /** Restarts all open nested sessions after a global index selection change. */
    public static void restartAll() {
        for (NestedSqlEditorLspBridge bridge : ACTIVE_BRIDGES.snapshot()) {
            bridge.restart();
        }
    }

    /** Requests fresh pull diagnostics from every open nested editor. */
    public static void refreshAllDiagnostics() {
        for (NestedSqlEditorLspBridge bridge : ACTIVE_BRIDGES.snapshot()) {
            bridge.analyze();
        }
    }

    /** Requests diagnostics for the current nested-editor document. */
    public void analyze() {
        if (!isSessionActive()) {
            return;
        }

        session.analyze();
    }

    /** Rebuilds the active index through the LSP and refreshes this nested session. */
    public void reindex() {
        if (isSessionActive()) {
            session.reindex();
        }
    }

    /** Resolves the repository root from the active nested-editor LSP session. */
    public CompletableFuture<String> repositoryRoot() {
        if (!isSessionActive()) {
            return CompletableFuture.completedFuture(null);
        }

        return session.repositoryRoot();
    }

    /** Reveals the first diagnostic in the nested editor. */
    public void showDiagnostics() {
        if (!isSessionActive()) {
            return;
        }

        session.showDiagnostics();
    }

    /** Reveals the next diagnostic in the nested editor. */
    public void showNextDiagnostic() {
        if (!isSessionActive()) {
            return;
        }

        session.diagnosticController().showNextDiagnostic();
    }

    /** Reveals the previous diagnostic in the nested editor. */
    public void showPreviousDiagnostic() {
        if (!isSessionActive()) {
            return;
        }

        session.diagnosticController().showPreviousDiagnostic();
    }

    /** Opens code actions for the current nested-editor selection. */
    public void showCodeActions() {
        if (!canShowCodeActions()) {
            return;
        }

        session.codeActionController().show(session.processManager());
    }

    /** Opens actions for every finding overlapping the current selection. */
    public void showAllCodeActions() {
        if (!canShowCodeActions()) {
            return;
        }

        session.codeActionController().showAll(session.processManager());
    }

    /** Formats the whole script through the sqldeps formatting code action. */
    public void formatScript() {
        if (!canShowCodeActions()) {
            return;
        }

        session.codeActionController().formatScript(session.processManager());
    }

    /** Shows the impact of changing the object under the selection or cursor. */
    public void showImpact() {
        if (!canShowCodeActions()) {
            return;
        }

        session.codeActionController().showImpact(session.processManager());
    }

    boolean showCodeActionsAt(int offset, int length) {
        if (!canShowCodeActions()) {
            log("sqldeps trace: nested quick fix ignored because the LSP session is not ready");

            return false;
        }

        return session.codeActionController().showAt(session.processManager(), offset, length);
    }

    /** Opens code actions for a DBeaver quick-fix request targeting a nested editor. */
    public static boolean showCodeActionsFor(SQLEditorBase editor, int offset, int length) {
        if (!(editor instanceof SQLEditorNested<?> nestedEditor)) {
            return false;
        }

        NestedSqlEditorLspBridge bridge = forEditor(nestedEditor);
        if (bridge == null) {
            return false;
        }

        return bridge.showCodeActionsAt(offset, length);
    }

    private boolean isSessionActive() {
        return !closed && session != null;
    }

    private boolean canShowCodeActions() {
        return isSessionActive() && session.codeActionController() != null;
    }

    /** Selects an explicit index and restarts this nested session. */
    public void selectIndex() {
        String selectedPath = IndexSelector.choose(
            PlatformUI.getWorkbench().getActiveWorkbenchWindow().getShell()
        );
        if (selectedPath == null) {
            return;
        }

        IndexSelectionState.select(selectedPath);
        restart();
    }

    /** Restores automatic index selection and restarts this nested session. */
    public void useAutomaticIndexSelection() {
        IndexSelectionState.useAutomaticSelection();
        restart();
    }

    /** Restarts the LSP document lifecycle while preserving the editor bridge. */
    public void restart() {
        if (closed) {
            return;
        }

        if (session != null) {
            session.restart();
        }
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        if (session != null) {
            session.close();
        }
    }

    private void activate() {
        if (closed || session != null || startScheduled) {
            return;
        }

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        startScheduled = true;
        display.asyncExec(() -> {
            if (closed || session != null) {
                startScheduled = false;
                return;
            }

            document = editor.getDocumentProvider().getDocument(editor.getEditorInput());
            if (document == null) {
                startScheduled = false;
                log("sqldeps nested SQL editor document is not ready; LSP was not started");
                return;
            }

            scheduler.execute(() -> {
                try {
                    if (!closed && session == null) {
                        startSession();
                    }
                }

                finally {
                    startScheduled = false;
                }
            });
        });
    }

    private void startSession() {
        log("sqldeps nested SQL editor detected; starting LSP analysis");
        session = new EditorLspSession(
            editor,
            document,
            documentUri,
            workspaceUri,
            scheduler,
            "nested diagnostics",
            this::log
        );
        session.initialize();
        session.start();
    }

    private void log(String message) {
        if (Activator.getDefault() == null) {
            return;
        }

        Activator.getDefault().getLog().log(new Status(IStatus.INFO, Activator.PLUGIN_ID, message));
    }
}
