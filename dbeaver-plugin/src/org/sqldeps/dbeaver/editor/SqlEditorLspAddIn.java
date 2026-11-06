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

import java.io.PrintWriter;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.WeakHashMap;
import java.util.concurrent.ScheduledExecutorService;

import org.eclipse.core.resources.IFile;
import org.eclipse.core.runtime.IStatus;
import org.eclipse.core.runtime.Status;
import org.eclipse.jface.text.IDocument;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditor;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.jkiss.dbeaver.ui.editors.sql.addins.SQLEditorAddIn;
import org.sqldeps.dbeaver.Activator;
import org.sqldeps.dbeaver.lsp.DocumentUri;
import org.sqldeps.dbeaver.editor.actions.CodeActionController;
import org.sqldeps.dbeaver.editor.index.IndexSelectionState;
import org.sqldeps.dbeaver.editor.index.IndexSelector;

/** DBeaver SQL Editor entry point that attaches one editor session to the shared LSP pool. */
public final class SqlEditorLspAddIn implements SQLEditorAddIn {
    private static final Map<SQLEditor, SqlEditorLspAddIn> ACTIVE_ADD_INS = new WeakHashMap<>();

    private final ScheduledExecutorService scheduler = SharedLspScheduler.instance();

    private EditorLspSession session;
    private CodeActionController codeActionController;
    private volatile boolean closed;

    /** Initializes synchronization, diagnostics, and code actions for the editor. */
    @Override
    public void init(SQLEditor editor) {
        IDocument document = editor.getDocumentProvider().getDocument(editor.getEditorInput());
        session = new EditorLspSession(
            editor,
            document,
            documentUri(editor),
            workspaceUri(editor),
            scheduler,
            "diagnostics",
            this::log
        );
        session.initialize();
        codeActionController = new CodeActionController(
            editor,
            document,
            session.synchronizer(),
            scheduler,
            () -> session.diagnosticController().currentDiagnostics(),
            () -> !closed,
            this::log
        );

        synchronized (ACTIVE_ADD_INS) {
            ACTIVE_ADD_INS.put(editor, this);
        }

        scheduler.execute(session::start);
    }

    /** Requests a fresh diagnostic report for the current document snapshot. */
    public void analyze() {
        if (closed || session == null) {
            return;
        }

        session.analyze();
    }

    /** Rebuilds the active index through the LSP and refreshes this editor session. */
    public void reindex() {
        if (!closed && session != null) {
            session.reindex();
        }
    }

    /** Opens the safe code-action list for the active document. */
    public void showCodeActions() {
        codeActionController.show(session.processManager());
    }

    void showCodeActionsAt(int offset, int length) {
        if (closed || session == null || codeActionController == null) {
            return;
        }

        codeActionController.showAt(session.processManager(), offset, length);
    }

    public static boolean showCodeActionsFor(SQLEditorBase editor, int offset, int length) {
        if (!(editor instanceof SQLEditor sqlEditor)) {
            return false;
        }

        SqlEditorLspAddIn addIn = forEditor(sqlEditor);
        if (addIn == null) {
            return false;
        }

        addIn.showCodeActionsAt(offset, length);
        return true;
    }

    /** Reveals the first diagnostic in the current editor. */
    public void showDiagnostics() {
        session.showDiagnostics();
    }

    /** Reveals the next diagnostic relative to the current selection. */
    public void showNextDiagnostic() {
        session.diagnosticController().showNextDiagnostic();
    }

    /** Reveals the previous diagnostic relative to the current selection. */
    public void showPreviousDiagnostic() {
        session.diagnosticController().showPreviousDiagnostic();
    }

    /** Selects a concrete SQLite index and restarts this editor session. */
    public void selectIndex() {
        String selectedPath = IndexSelector.choose(
            PlatformUI.getWorkbench().getActiveWorkbenchWindow().getShell()
        );
        if (selectedPath == null) {
            return;
        }

        IndexSelectionState.select(selectedPath);
        session.restart();
    }

    /** Restores workspace-based index selection and restarts this editor session. */
    public void useAutomaticIndexSelection() {
        IndexSelectionState.useAutomaticSelection();
        session.restart();
    }

    @Override
    public void cleanup(SQLEditor editor) {
        closed = true;

        synchronized (ACTIVE_ADD_INS) {
            ACTIVE_ADD_INS.remove(editor);
        }

        if (session != null) {
            session.close();
        }
    }

    @Override
    public PrintWriter getServerOutputConsumer() {
        return null;
    }

    /** Returns the live add-in for an editor, if that editor has been initialized. */
    public static SqlEditorLspAddIn forEditor(SQLEditor editor) {
        synchronized (ACTIVE_ADD_INS) {
            return ACTIVE_ADD_INS.get(editor);
        }
    }

    /** Restarts all open SQL editors after a global LSP setting change. */
    public static void restartAll() {
        List<SqlEditorLspAddIn> addIns;
        synchronized (ACTIVE_ADD_INS) {
            addIns = List.copyOf(ACTIVE_ADD_INS.values());
        }

        for (SqlEditorLspAddIn addIn : addIns) {
            addIn.restart();
        }
    }

    static List<String> configuredCommand() {
        String executable = System.getProperty("sqldeps.lsp.path");
        if (executable == null || executable.isBlank()) {
            executable = System.getenv("SQLDEPS_LSP_PATH");
        }

        if (executable == null || executable.isBlank()) {
            Path localExecutable = Path.of(
                System.getProperty("user.home"),
                ".local",
                "bin",
                "sqldeps"
            );
            executable = Files.isExecutable(localExecutable) ? localExecutable.toString() : "sqldeps";
        }

        return List.of(executable, "lsp");
    }

    private void restart() {
        if (!closed && session != null) {
            session.restart();
        }
    }

    private static String documentUri(SQLEditor editor) {
        IFile file = editor.getEditorInput().getAdapter(IFile.class);
        if (file != null && file.getLocationURI() != null) {
            return file.getLocationURI().normalize().toString();
        }

        return DocumentUri.forUnsavedEditor(Integer.toHexString(System.identityHashCode(editor)));
    }

    private static String workspaceUri(SQLEditor editor) {
        IFile file = editor.getEditorInput().getAdapter(IFile.class);
        if (file == null || file.getProject() == null || file.getProject().getLocationURI() == null) {
            return null;
        }

        return file.getProject().getLocationURI().normalize().toString();
    }

    private void log(String message) {
        if (Activator.getDefault() == null) {
            return;
        }

        Activator.getDefault().getLog().log(new Status(IStatus.INFO, Activator.PLUGIN_ID, message));
    }
}

