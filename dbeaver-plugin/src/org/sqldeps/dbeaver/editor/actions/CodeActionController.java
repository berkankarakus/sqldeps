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

import java.util.ArrayList;
import java.util.Map;
import java.util.List;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.jface.window.Window;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionContext;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.swt.SWT;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.TraceSupport;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;

/** Requests, filters, previews, and applies safe active-document code actions. */
public final class CodeActionController {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final ScheduledExecutorService scheduler;
    private final Supplier<List<Diagnostic>> diagnosticsSupplier;
    private final BooleanSupplier activeSupplier;
    private final Consumer<String> logConsumer;
    private final WorkspaceEditApplier workspaceEditApplier;
    private boolean dialogOpen;

    /** Creates a controller bound to one editor document and its LSP session. */
    public CodeActionController(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        ScheduledExecutorService scheduler,
        Supplier<List<Diagnostic>> diagnosticsSupplier,
        BooleanSupplier activeSupplier,
        Runnable refreshDiagnostics,
        Consumer<String> logConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.scheduler = scheduler;
        this.diagnosticsSupplier = diagnosticsSupplier;
        this.activeSupplier = activeSupplier;
        this.refreshDiagnostics = refreshDiagnostics;
        this.logConsumer = logConsumer;
        this.workspaceEditApplier = new WorkspaceEditApplier(
            editor,
            document,
            synchronizer,
            refreshDiagnostics,
            logConsumer,
            this::trace);
    }

    /** Requests actions for the current selection and displays safe choices. */
    public void show(LspConnection processManager) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: code actions are not ready yet");
            trace("code actions requested before the LSP session was ready");
            return;
        }

        try {
            show(processManager, selectedRange(), diagnosticsSupplier.get());
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action range could not be read: " + exception.getMessage());
        }
    }

    /** Runs the impact analysis for the object name under the selection or cursor. */
    public void showImpact(LspConnection processManager) {
        requestSelectionActions(processManager, "impact analysis", (snapshot, actions) -> {
            CodeActionChoice impactChoice = findCommandChoice(actions, "sqldeps.impact");

            if (impactChoice == null) {
                reportStatus("sqldeps: place the cursor on an indexed table, view or procedure name");

                return;
            }

            executeCommand(snapshot, processManager, impactChoice.command());
        });
    }

    /** Formats the whole script through the language server; scripts that do not parse are left untouched. */
    public void formatScript(LspConnection processManager) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: formatting is not ready yet");

            return;
        }

        DocumentSnapshot snapshot = synchronizer.snapshot();
        trace("requesting document formatting");

        scheduler.execute(() -> processManager.requestFormatting(snapshot.uri()).whenComplete((edits, exception) -> {
            if (exception != null) {
                reportFailure("sqldeps Format", "formatting", exception);

                return;
            }

            if (edits == null || edits.isEmpty()) {
                reportStatus("sqldeps: the script is already formatted");

                return;
            }

            WorkspaceEdit edit = new WorkspaceEdit();
            edit.setChanges(Map.of(snapshot.uri(), new ArrayList<TextEdit>(edits)));

            Display display = Display.getDefault();

            if (display.isDisposed()) {
                return;
            }

            display.asyncExec(() -> applyWorkspaceEdit(snapshot, "Format SQL script", edit));
        }));
    }

    private void requestSelectionActions(
        LspConnection processManager,
        String purpose,
        BiConsumer<DocumentSnapshot, List<Either<Command, CodeAction>>> handler
    ) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: " + purpose + " is not ready yet");

            return;
        }

        Range range;

        try {
            range = selectedRange();
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps " + purpose + " range could not be read: " + exception.getMessage());

            return;
        }

        DocumentSnapshot snapshot = synchronizer.snapshot();
        CodeActionParams params = new CodeActionParams();
        params.setTextDocument(new TextDocumentIdentifier(snapshot.uri()));
        params.setRange(range);
        params.setContext(new CodeActionContext(List.of()));
        trace("requesting " + purpose);

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                reportFailure("sqldeps", purpose, exception);

                return;
            }

            handler.accept(snapshot, actions == null ? List.of() : actions);
        }));
    }

    private CodeActionChoice findCommandChoice(List<Either<Command, CodeAction>> actions, String commandName) {
        for (CodeActionChoice choice : buildChoices(actions, List.of())) {
            Command command = choice.command();

            if (command != null && commandName.equals(command.getCommand())) {
                return choice;
            }
        }

        return null;
    }

    /** Logs a failed request and shows the reason in a dialog, because a status bar message is easy to miss. */
    private void reportFailure(String title, String purpose, Throwable failure) {
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
            if (editor.getEditorSite() == null) {
                return;
            }

            Shell shell = editor.getEditorSite().getShell();

            if (shell == null || shell.isDisposed()) {
                return;
            }

            MessageDialog.openError(shell, title, message);
        });
    }

    private void reportStatus(String message) {
        Display display = Display.getDefault();

        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> setStatus(message));
    }

    /** Requests actions for a diagnostic range supplied by DBeaver's quick-fix UI. */
    public boolean showAt(LspConnection processManager, int offset, int length) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: code actions are not ready yet");
            trace("diagnostic quick fix requested before the LSP session was ready");

            return false;
        }

        try {
            Range range = rangeAt(offset, length);
            trace("diagnostic quick fix requested at offset " + offset + ", length " + length);
            show(processManager, range, diagnosticsForRange(range));

            return true;
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action range could not be read: " + exception.getMessage());

            return false;
        }
    }

    private void show(
        LspConnection processManager,
        Range range,
        List<Diagnostic> diagnostics
    ) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        CodeActionParams params = new CodeActionParams();
        params.setTextDocument(new TextDocumentIdentifier(snapshot.uri()));
        params.setRange(range);
        params.setContext(new CodeActionContext(List.copyOf(diagnostics)));
        trace("requesting code actions for " + diagnostics.size() + " diagnostic(s)");

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                logConsumer.accept("sqldeps code action request failed: " + exception.getMessage());
                return;
            }

            trace("received " + (actions == null ? 0 : actions.size()) + " raw code action(s)");
            try {
                showDialog(snapshot, processManager, actions, diagnostics);
            }

            catch (RuntimeException runtimeException) {
                logConsumer.accept("sqldeps code action UI failed: " + runtimeException.getMessage());
                trace("code action UI failed: " + runtimeException.getClass().getSimpleName());
            }
        }));
    }

    private Range selectedRange() throws BadLocationException {
        ISelection selection = editor.getSite().getSelectionProvider().getSelection();
        int offset = selectionOffset(selection);
        int length = selectionLength(selection);

        int start = Math.max(0, Math.min(offset, document.getLength()));
        int end = Math.max(start, Math.min(start + length, document.getLength()));
        return rangeAt(start, end - start);
    }

    private static int selectionOffset(ISelection selection) {
        if (selection instanceof ITextSelection textSelection) {
            return textSelection.getOffset();
        }

        return 0;
    }

    private static int selectionLength(ISelection selection) {
        if (selection instanceof ITextSelection textSelection) {
            return textSelection.getLength();
        }

        return 0;
    }

    private Range rangeAt(int offset, int length) throws BadLocationException {
        int start = Math.max(0, Math.min(offset, document.getLength()));
        long requestedEnd = (long)start + Math.max(0, length);
        int end = (int)Math.min(document.getLength(), requestedEnd);

        return new Range(positionAt(start), positionAt(end));
    }

    private Position positionAt(int offset) throws BadLocationException {
        int line = document.getLineOfOffset(offset);
        return new Position(line, offset - document.getLineOffset(line));
    }

    private List<Diagnostic> diagnosticsForRange(Range range) throws BadLocationException {
        return DiagnosticRangeSelection.select(document, range, diagnosticsSupplier.get());
    }

    private void showDialog(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        List<CodeActionChoice> choices = buildChoices(actions == null ? List.of() : actions, diagnostics);
        trace("built " + choices.size() + " displayable code action(s)");

        if (choices.isEmpty()) {
            trace("no safe code action was available");
        }

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> {
            boolean active = activeSupplier.getAsBoolean();
            boolean current = synchronizer.isCurrent(snapshot);
            trace("code action UI state: active=" + active + ", current=" + current);
            if (!active || !current) {
                return;
            }

            if (editor.getEditorSite() == null) {
                trace("code action UI skipped: editor site is unavailable");

                return;
            }

            Shell shell = editor.getEditorSite().getShell();
            if (shell == null || shell.isDisposed()) {
                trace("code action UI skipped: editor shell is unavailable");

                return;
            }

            if (choices.isEmpty()) {
                showNoCodeActionMessage(shell, diagnostics);

                return;
            }

            if (CodeActionSelection.modeFor(choices) == CodeActionSelection.Mode.APPLY_DIRECTLY) {
                trace("applying the only available code action: " + choices.get(0).title());
                applyChoice(snapshot, processManager, choices.get(0));
                return;
            }

            if (dialogOpen) {
                trace("code action UI skipped: a dialog is already open");

                return;
            }

            CodeActionSelectionDialog dialog = new CodeActionSelectionDialog(
                shell,
                choices
            );
            dialog.setTitle("sqldeps code actions");
            dialogOpen = true;

            try {
                // Window.getShell() is null until create() runs, so create the shell first.
                dialog.create();

                Shell dialogShell = dialog.getShell();
                dialogShell.addListener(SWT.Show, event -> activateDialog(dialogShell));

                trace("opening code action dialog");
                int result = dialog.open();
                trace("code action dialog closed with result " + result);

                CodeActionChoice choice = CodeActionSelection.confirmedChoice(
                    result == Window.OK,
                    dialog.getResult()
                );

                if (choice != null) {
                    applyChoice(snapshot, processManager, choice);
                }
            }

            catch (RuntimeException exception) {
                trace("code action dialog failed: " + exception);
                setStatus("sqldeps: code action dialog failed: " + exception.getMessage());
            }

            finally {
                dialogOpen = false;
            }
        });
    }

    private static void showNoCodeActionMessage(Shell shell, List<Diagnostic> diagnostics) {
        String message = "No safe quick fix is available for this finding.";

        if (!diagnostics.isEmpty() && diagnostics.get(0).getMessage() != null) {
            message += "\n\nFinding:\n" + diagnostics.get(0).getMessage();
        }

        MessageDialog.openInformation(shell, "sqldeps Quick Fix", message);
    }

    private static void activateDialog(Shell dialogShell) {
        if (dialogShell.isDisposed()) {
            return;
        }

        dialogShell.forceActive();
        dialogShell.forceFocus();
    }

    private void applyChoice(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        CodeActionChoice choice
    ) {
        if (choice.action() != null) {
            applyWorkspaceEdit(snapshot, choice.action().getTitle(), choice.action().getEdit());
            return;
        }

        executeCommand(snapshot, processManager, choice.command());
    }

    private List<CodeActionChoice> buildChoices(
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        return new CodeActionChoiceBuilder(document).build(actions, diagnostics);
    }

    private boolean isPreviewCommand(String command) {
        return SqldepsCommands.isPreview(command);
    }

    private static boolean isSupportedCommand(Command command) {
        return command != null && isKnownCommand(command.getCommand());
    }

    private static boolean isKnownCommand(String command) {
        return SqldepsCommands.isKnown(command);
    }

    private boolean isReady(LspConnection processManager) {
        return activeSupplier.getAsBoolean() && processManager != null;
    }

    private void executeCommand(
        DocumentSnapshot snapshot,
        LspConnection processManager,
        Command command
    ) {
        if (!activeSupplier.getAsBoolean() || !synchronizer.isCurrent(snapshot) || !isSupportedCommand(command)) {
            logConsumer.accept("sqldeps rejected an unsupported or stale code action command");
            return;
        }

        processManager.executeCommand(command).whenComplete((result, exception) -> {
            if (exception != null) {
            logConsumer.accept("sqldeps code action command failed: " + exception.getMessage());
                return;
            }

            if (!isPreviewCommand(command.getCommand()) || !(result instanceof String sql)) {
                return;
            }

            Display display = Display.getDefault();
            if (display.isDisposed()) {
                return;
            }

            display.asyncExec(() -> {
                if (!activeSupplier.getAsBoolean() || !synchronizer.isCurrent(snapshot)) {
                    return;
                }

                var window = PlatformUI.getWorkbench().getActiveWorkbenchWindow();
                if (window == null || window.getShell() == null || window.getShell().isDisposed()) {
                    return;
                }

                new TextResultDialog(window.getShell(), command.getTitle(), sql).open();
            });
        });
    }

    private void applyWorkspaceEdit(
        DocumentSnapshot snapshot,
        String actionTitle,
        WorkspaceEdit edit
    ) {
        workspaceEditApplier.apply(snapshot, actionTitle, edit);
    }

    private void setStatus(String message) {
        editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
    }

    private void trace(String message) {
        TraceSupport.log(logConsumer, message);
    }

}

