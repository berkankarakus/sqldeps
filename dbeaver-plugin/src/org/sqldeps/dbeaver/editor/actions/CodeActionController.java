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

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.List;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.Document;
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
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.ui.PlatformUI;
import org.eclipse.ui.texteditor.IDocumentProvider;
import org.eclipse.ui.texteditor.IDocumentProviderExtension;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
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

    /** Creates a controller bound to one editor document and its LSP session. */
    public CodeActionController(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        ScheduledExecutorService scheduler,
        Supplier<List<Diagnostic>> diagnosticsSupplier,
        BooleanSupplier activeSupplier,
        Consumer<String> logConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.scheduler = scheduler;
        this.diagnosticsSupplier = diagnosticsSupplier;
        this.activeSupplier = activeSupplier;
        this.logConsumer = logConsumer;
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
            setStatus("sqldeps: no code action is available for this diagnostic");
            trace("no safe code action was available");
            return;
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

            if (choices.size() == 1) {
                trace("applying the only available code action: " + choices.get(0).title());
                applyChoice(snapshot, processManager, choices.get(0));
                return;
            }

            CodeActionSelectionDialog dialog = new CodeActionSelectionDialog(
                shell,
                choices
            );
            dialog.setTitle("sqldeps code actions");
            dialog.getShell().addListener(SWT.Show, event -> activateDialog(dialog.getShell()));
            trace("opening code action dialog");
            int result = dialog.open();
            trace("code action dialog closed with result " + result);
            if (result == Window.OK
                && dialog.getResult() != null
                && dialog.getResult().length > 0
                && dialog.getResult()[0] instanceof CodeActionChoice choice) {
                applyChoice(snapshot, processManager, choice);
            }
        });
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
        if (command == null) {
            return false;
        }

        return switch (command) {
            case "sqldeps.query-builder", "sqldeps.index-suggestion", "sqldeps.explain-diagnostic",
                "sqldeps.repository-context", "sqldeps.who-writes", "sqldeps.who-reads",
                "sqldeps.impact", "sqldeps.column-writers" -> true;
            default -> false;
        };
    }

    private static boolean isSupportedCommand(Command command) {
        return command != null && isKnownCommand(command.getCommand());
    }

    private static boolean isKnownCommand(String command) {
        if (command == null) {
            return false;
        }

        return switch (command) {
            case "sqldeps.query-builder", "sqldeps.index-suggestion", "sqldeps.explain-diagnostic",
                "sqldeps.repository-context", "sqldeps.who-writes", "sqldeps.who-reads",
                "sqldeps.impact", "sqldeps.column-writers", "sqldeps.reindex" -> true;
            default -> false;
        };
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

                MessageDialog.openInformation(window.getShell(), command.getTitle(), sql);
            });
        });
    }

    private void applyWorkspaceEdit(
        DocumentSnapshot snapshot,
        String actionTitle,
        WorkspaceEdit edit
    ) {
        if (!isDocumentEditable()) {
            String message = "sqldeps: this SQL document is read-only; the Quick Fix was not applied";
            logConsumer.accept(message);
            setStatus(message);
            trace("blocked code action on a read-only document: " + actionTitle);
            return;
        }

        WorkspaceEditSafety.ExternalEdit externalEdit = WorkspaceEditSafety.resolveExternalConfigurationEdit(
            snapshot,
            synchronizer.isCurrent(snapshot),
            edit);
        if (externalEdit != null) {
            applyExternalEdit(actionTitle, externalEdit);
            return;
        }

        if (!WorkspaceEditSafety.isApplicable(snapshot, synchronizer.isCurrent(snapshot), edit)) {
            logConsumer.accept("sqldeps code action change does not match the current document; not applied");
            return;
        }

        List<TextEdit> textEdits = edit.getChanges().get(snapshot.uri());
        List<WorkspaceEditSafety.ResolvedTextEdit> resolvedEdits = WorkspaceEditSafety.resolve(document, textEdits);
        if (resolvedEdits == null) {
            logConsumer.accept("sqldeps code action edits are unsafe; not applied");
            return;
        }

        if (!confirmWorkspaceEdit(actionTitle, resolvedEdits)) {
            return;
        }

        try {
            applyEdits(document, resolvedEdits);

            editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(
                "sqldeps change applied; use Undo to revert");
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action could not be applied: " + exception.getMessage());
        }
    }

    private void setStatus(String message) {
        editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
    }

    private void trace(String message) {
        if (Boolean.getBoolean("sqldeps.lsp.trace")) {
            String traceMessage = "sqldeps trace: " + message;
            logConsumer.accept(traceMessage);
            System.err.println(traceMessage);
        }
    }

    private void applyExternalEdit(
        String actionTitle,
        WorkspaceEditSafety.ExternalEdit externalEdit
    ) {
        IDocument target = new Document(externalEdit.originalText());
        if (!confirmWorkspaceEdit(actionTitle + " — " + externalEdit.path(), target, externalEdit.edits())) {
            return;
        }

        try {
            String currentText = Files.readString(externalEdit.path(), StandardCharsets.UTF_8);
            if (!currentText.equals(externalEdit.originalText())) {
                logConsumer.accept("sqldeps external code action target changed; not applied");
                return;
            }

            applyEdits(target, externalEdit.edits());
            Files.writeString(externalEdit.path(), target.get(), StandardCharsets.UTF_8);
            editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(
                "sqldeps configuration change applied: " + externalEdit.path());
        }

        catch (Exception exception) {
            logConsumer.accept("sqldeps external code action could not be applied: " + exception.getMessage());
        }
    }

    private static void applyEdits(
        IDocument target,
        List<WorkspaceEditSafety.ResolvedTextEdit> edits
    ) throws BadLocationException {
        for (WorkspaceEditSafety.ResolvedTextEdit edit : edits) {
            target.replace(edit.start(), edit.end() - edit.start(), edit.newText());
        }
    }

    private boolean confirmWorkspaceEdit(
        String actionTitle,
        List<WorkspaceEditSafety.ResolvedTextEdit> edits
    ) {
        return confirmWorkspaceEdit(actionTitle, document, edits);
    }

    private boolean confirmWorkspaceEdit(
        String actionTitle,
        IDocument target,
        List<WorkspaceEditSafety.ResolvedTextEdit> edits
    ) {
        String preview = workspaceEditPreview(target, edits);
        if (preview == null) {
            logConsumer.accept("sqldeps code action preview could not be created; not applied");

            return false;
        }

        if (editor.getEditorSite() == null) {
            return false;
        }

        Shell shell = editor.getEditorSite().getShell();
        if (shell == null || shell.isDisposed()) {
            return false;
        }

        return MessageDialog.openQuestion(
            shell,
            "Preview: " + actionTitle,
            preview + "\n\nApply this change? The editor Undo command can revert it.");
    }

    private String workspaceEditPreview(
        IDocument target,
        List<WorkspaceEditSafety.ResolvedTextEdit> edits
    ) {
        StringBuilder preview = new StringBuilder("The following edit will be applied:\n");

        for (int index = edits.size() - 1; index >= 0; index--) {
            WorkspaceEditSafety.ResolvedTextEdit edit = edits.get(index);

            try {
                String oldText = target.get(edit.start(), edit.end() - edit.start());
                int line = target.getLineOfOffset(edit.start()) + 1;
                preview.append("\nline ").append(line).append(":\n");
                preview.append("- ").append(previewText(oldText)).append('\n');
                preview.append("+ ").append(previewText(edit.newText())).append('\n');
            }

            catch (BadLocationException exception) {
                return null;
            }
        }

        return preview.toString();
    }

    private static String previewText(String text) {
        String compact = text.replace("\r", "\\r").replace("\n", "\\n");

        if (compact.length() <= 400) {
            return compact.isEmpty() ? "(empty)" : compact;
        }

        return compact.substring(0, 397) + "...";
    }

    private boolean isDocumentEditable() {
        IDocumentProvider provider = editor.getDocumentProvider();
        if (provider instanceof IDocumentProviderExtension extension) {
            return !extension.isReadOnly(editor.getEditorInput());
        }

        return true;
    }

}

