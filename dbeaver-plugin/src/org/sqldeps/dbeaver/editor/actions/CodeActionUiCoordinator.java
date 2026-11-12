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
package org.sqldeps.dbeaver.editor.actions;

import java.util.List;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;

import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Shell;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;

/** Coordinates code-action dialogs and UI-safe action application. */
final class CodeActionUiCoordinator {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final BooleanSupplier activeSupplier;
    private final Consumer<String> logConsumer;
    private final Consumer<String> traceConsumer;
    private final CodeActionCommandExecutor commandExecutor;
    private final CodeActionDialogPresenter dialogPresenter;
    private boolean dialogOpen;

    CodeActionUiCoordinator(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        BooleanSupplier activeSupplier,
        Consumer<String> logConsumer,
        Consumer<String> traceConsumer,
        CodeActionCommandExecutor commandExecutor,
        CodeActionDialogPresenter dialogPresenter
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.activeSupplier = activeSupplier;
        this.logConsumer = logConsumer;
        this.traceConsumer = traceConsumer;
        this.commandExecutor = commandExecutor;
        this.dialogPresenter = dialogPresenter;
    }

    void show(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        List<CodeActionChoice> choices = new CodeActionChoiceBuilder(document).build(
            actions == null ? List.of() : actions,
            diagnostics);
        traceConsumer.accept("built " + choices.size() + " displayable code action(s)");

        if (choices.isEmpty()) {
            traceConsumer.accept("no safe code action was available");
        }

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> showOnUiThread(snapshot, processManager, choices, diagnostics));
    }

    void reportFailure(String title, String purpose, Throwable failure) {
        Throwable cause = failure;

        while (cause instanceof java.util.concurrent.CompletionException && cause.getCause() != null) {
            cause = cause.getCause();
        }

        String reason = cause.getMessage();

        if (reason == null || reason.isBlank()) {
            reason = cause.getClass().getSimpleName();
        }

        String message = "sqldeps " + purpose + " failed: " + reason;
        logConsumer.accept(message);

        Display display = Display.getDefault();

        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> {
            Shell shell = editorShell();

            if (shell == null || shell.isDisposed()) {
                return;
            }

            MessageDialog.openError(shell, title, message);
        });
    }

    void reportStatus(String message) {
        Display display = Display.getDefault();

        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> setStatus(message));
    }

    private void showOnUiThread(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        List<CodeActionChoice> choices,
        List<Diagnostic> diagnostics
    ) {
        boolean active = activeSupplier.getAsBoolean();
        boolean current = synchronizer.isCurrent(snapshot);
        traceConsumer.accept("code action UI state: active=" + active + ", current=" + current);

        if (!active || !current) {
            return;
        }

        Shell shell = editorShell();

        if (shell == null || shell.isDisposed()) {
            traceConsumer.accept("code action UI skipped: editor shell is unavailable");

            return;
        }

        if (choices.isEmpty()) {
            CodeActionDialogPresenter.showNoCodeActionMessage(shell, diagnostics);

            return;
        }

        if (CodeActionSelection.modeFor(choices) == CodeActionSelection.Mode.APPLY_DIRECTLY) {
            traceConsumer.accept("applying the only available code action: " + choices.get(0).title());
            applyChoice(snapshot, processManager, choices.get(0));

            return;
        }

        if (dialogOpen) {
            traceConsumer.accept("code action UI skipped: a dialog is already open");

            return;
        }

        dialogOpen = true;

        try {
            CodeActionChoice choice = dialogPresenter.open(shell, choices, traceConsumer);

            if (choice != null) {
                applyChoice(snapshot, processManager, choice);
            }
        }

        catch (RuntimeException exception) {
            traceConsumer.accept("code action dialog failed: " + exception);
            setStatus("sqldeps: code action dialog failed: " + exception.getMessage());
        }

        finally {
            dialogOpen = false;
        }
    }

    private void applyChoice(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        CodeActionChoice choice
    ) {
        if (choice.action() != null) {
            commandExecutor.applyWorkspaceEdit(snapshot, choice.action().getTitle(), choice.action().getEdit());

            return;
        }

        commandExecutor.execute(snapshot, processManager, choice.command());
    }

    private Shell editorShell() {
        if (editor.getEditorSite() == null) {
            traceConsumer.accept("code action UI skipped: editor site is unavailable");

            return null;
        }

        return editor.getEditorSite().getShell();
    }

    private void setStatus(String message) {
        if (editor.getEditorSite() != null) {
            editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
        }
    }
}
