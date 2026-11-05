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
package org.sqldeps.dbeaver.ui;

import java.util.Map;
import java.util.WeakHashMap;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

import org.eclipse.core.runtime.IStatus;
import org.eclipse.core.runtime.Status;
import org.eclipse.jface.text.IDocument;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;
import org.sqldeps.dbeaver.Activator;
import org.sqldeps.dbeaver.lsp.DocumentUri;

public final class NestedSqlEditorLspBridge implements AutoCloseable {
    private static final Map<SQLEditorNested<?>, NestedSqlEditorLspBridge> ACTIVE_BRIDGES = new WeakHashMap<>();
    private static volatile NestedSqlEditorLspBridge activeBridge;

    private final SQLEditorNested<?> editor;
    private final ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor(
        runnable -> {
            Thread thread = new Thread(runnable, "sqldeps-dbeaver-nested");
            thread.setDaemon(true);
            return thread;
        }
    );
    private final String documentUri;

    private IDocument document;
    private EditorLspSession session;
    private boolean closed;

    private NestedSqlEditorLspBridge(SQLEditorNested<?> editor) {
        this.editor = editor;
        documentUri = DocumentUri.forUnsavedEditor(
            "nested-" + Integer.toHexString(System.identityHashCode(editor))
        );
    }

    public static void attach(SQLEditorNested<?> editor) {
        synchronized (ACTIVE_BRIDGES) {
            NestedSqlEditorLspBridge existing = ACTIVE_BRIDGES.get(editor);
            if (existing != null) {
                activeBridge = existing;
                return;
            }

            NestedSqlEditorLspBridge bridge = new NestedSqlEditorLspBridge(editor);
            ACTIVE_BRIDGES.put(editor, bridge);
            activeBridge = bridge;
            bridge.scheduleStart(0);
        }
    }

    public static NestedSqlEditorLspBridge active() {
        return activeBridge;
    }

    public static void clearActive() {
        activeBridge = null;
    }

    public static void detach(SQLEditorNested<?> editor) {
        NestedSqlEditorLspBridge bridge;
        synchronized (ACTIVE_BRIDGES) {
            bridge = ACTIVE_BRIDGES.remove(editor);
        }

        if (bridge != null) {
            if (activeBridge == bridge) {
                activeBridge = null;
            }

            bridge.close();
        }
    }

    public static NestedSqlEditorLspBridge forEditor(SQLEditorNested<?> editor) {
        synchronized (ACTIVE_BRIDGES) {
            return ACTIVE_BRIDGES.get(editor);
        }
    }

    public static void restartAll() {
        List<NestedSqlEditorLspBridge> bridges;
        synchronized (ACTIVE_BRIDGES) {
            bridges = new ArrayList<>(ACTIVE_BRIDGES.values());
        }

        for (NestedSqlEditorLspBridge bridge : bridges) {
            bridge.restart();
        }
    }

    public void analyze() {
        if (closed || session == null) {
            return;
        }

        session.analyze();
    }

    public void showDiagnostics() {
        if (session != null) {
            session.showDiagnostics();
        }
    }

    public void showNextDiagnostic() {
        if (session != null) {
            session.diagnosticController().showNextDiagnostic();
        }
    }

    public void showPreviousDiagnostic() {
        if (session != null) {
            session.diagnosticController().showPreviousDiagnostic();
        }
    }

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

    public void useAutomaticIndexSelection() {
        IndexSelectionState.useAutomaticSelection();
        restart();
    }

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

    private void scheduleStart(int attempt) {
        if (closed) {
            return;
        }

        if (attempt > 20) {
            log("sqldeps nested SQL editor belgesi zamanında alınamadı; LSP başlatılmadı");
            return;
        }

        scheduler.schedule(
            () -> {
                document = editor.getDocumentProvider().getDocument(editor.getEditorInput());
                if (document == null) {
                    scheduleStart(attempt + 1);
                    return;
                }

                startSession();
            },
            attempt == 0 ? 0 : 250,
            TimeUnit.MILLISECONDS
        );
    }

    private void startSession() {
        log("sqldeps nested SQL editor algılandı; LSP analizi başlatılıyor");
        session = new EditorLspSession(
            editor,
            document,
            documentUri,
            null,
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
