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
package org.sqldeps.dbeaver.commands;

import java.nio.file.Path;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import org.eclipse.core.commands.AbstractHandler;
import org.eclipse.core.commands.ExecutionEvent;
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.handlers.HandlerUtil;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditor;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;
import org.sqldeps.dbeaver.editor.index.IndexCatalog;
import org.sqldeps.dbeaver.editor.index.IndexSelectionState;
import org.sqldeps.dbeaver.editor.index.RepositorySuppressionDialog;
import org.sqldeps.dbeaver.editor.index.RepositorySuppressionStore;

/** Opens the DBeaver editor for repository-level sqldeps suppressions. */
public final class ManageSuppressionsHandler extends AbstractHandler {
    private static final long ROOT_TIMEOUT_SECONDS = 5;

    @Override
    public Object execute(ExecutionEvent event) {
        Shell shell = HandlerUtil.getActiveShell(event);
        CompletableFuture<String> rootFuture = repositoryRootFromActiveSession(HandlerUtil.getActiveEditor(event));

        if (rootFuture == null) {
            openFromIndex(shell);

            return null;
        }

        openFromSession(shell, rootFuture);

        return null;
    }

    /** Finds the LSP session of the active editor, including nested source editors behind an entity editor. */
    private static CompletableFuture<String> repositoryRootFromActiveSession(IEditorPart editorPart) {
        SQLEditorBase sqlEditor = sqlEditorOf(editorPart);

        if (sqlEditor instanceof SQLEditor editor) {
            SqlEditorLspAddIn addIn = SqlEditorLspAddIn.forEditor(editor);

            if (addIn != null) {
                return addIn.repositoryRoot();
            }
        }

        if (sqlEditor instanceof SQLEditorNested<?> editor) {
            NestedSqlEditorLspBridge bridge = NestedSqlEditorLspBridge.forEditor(editor);

            if (bridge != null) {
                return bridge.repositoryRoot();
            }
        }

        NestedSqlEditorLspBridge activeBridge = NestedSqlEditorLspBridge.active();

        if (activeBridge != null) {
            return activeBridge.repositoryRoot();
        }

        return null;
    }

    private static SQLEditorBase sqlEditorOf(IEditorPart editorPart) {
        if (editorPart == null) {
            return null;
        }

        if (editorPart instanceof SQLEditorBase sqlEditor) {
            return sqlEditor;
        }

        return editorPart.getAdapter(SQLEditorBase.class);
    }

    private static void openFromSession(Shell shell, CompletableFuture<String> rootFuture) {
        CompletableFuture<String> boundedFuture = rootFuture.orTimeout(ROOT_TIMEOUT_SECONDS, TimeUnit.SECONDS);

        boundedFuture.whenComplete((root, exception) -> Display.getDefault().asyncExec(() -> {
            if (shell == null || shell.isDisposed()) {
                return;
            }

            if (exception != null || root == null || root.isBlank()) {
                openFromIndex(shell);

                return;
            }

            openDialog(shell, root);
        }));
    }

    private static void openFromIndex(Shell shell) {
        String indexPath = IndexSelectionState.indexPathForLsp();
        String sourceRoot = IndexCatalog.sourceRootFor(indexPath);

        if (sourceRoot == null || sourceRoot.isBlank()) {
            MessageDialog.openInformation(
                shell,
                "sqldeps Suppressions",
                RepositorySuppressionStore.unavailableRootMessage(indexPath)
            );

            return;
        }

        openDialog(shell, sourceRoot);
    }

    private static void openDialog(Shell shell, String sourceRoot) {
        boolean changed;

        try {
            changed = RepositorySuppressionDialog.open(shell, Path.of(sourceRoot));
        }

        catch (RuntimeException exception) {
            MessageDialog.openError(shell, "sqldeps Suppressions", exception.getMessage());

            return;
        }

        if (changed) {
            SqlEditorLspAddIn.refreshAllDiagnostics();
            NestedSqlEditorLspBridge.refreshAllDiagnostics();
        }
    }
}
