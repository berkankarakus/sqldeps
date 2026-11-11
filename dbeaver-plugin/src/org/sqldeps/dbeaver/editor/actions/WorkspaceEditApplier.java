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
import java.nio.file.StandardOpenOption;
import java.util.List;
import java.util.function.Consumer;

import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.Document;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.texteditor.IDocumentProvider;
import org.eclipse.ui.texteditor.IDocumentProviderExtension;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;

/** Validates, previews, and applies active-document or repository configuration edits. */
final class WorkspaceEditApplier {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final Runnable refreshDiagnostics;
    private final Consumer<String> logConsumer;
    private final Consumer<String> traceConsumer;

    WorkspaceEditApplier(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        Runnable refreshDiagnostics,
        Consumer<String> logConsumer,
        Consumer<String> traceConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.refreshDiagnostics = refreshDiagnostics;
        this.logConsumer = logConsumer;
        this.traceConsumer = traceConsumer;
    }

    void apply(DocumentSnapshot snapshot, String actionTitle, WorkspaceEdit edit) {
        apply(snapshot, actionTitle, edit, true);
    }

    void applyFormatting(DocumentSnapshot snapshot, WorkspaceEdit edit) {
        apply(snapshot, "Format SQL script", edit, false);
    }

    private void apply(
        DocumentSnapshot snapshot,
        String actionTitle,
        WorkspaceEdit edit,
        boolean requireConfirmation
    ) {
        WorkspaceEditSafety.ExternalEdit externalEdit = WorkspaceEditSafety.resolveExternalConfigurationEdit(
            snapshot,
            synchronizer.isCurrent(snapshot),
            edit);
        if (externalEdit != null) {
            applyExternalEdit(actionTitle, externalEdit);
            return;
        }

        if (!isDocumentEditable()) {
            String message = "sqldeps: this SQL document is read-only; the Quick Fix was not applied";
            logConsumer.accept(message);
            setStatus(message);
            traceConsumer.accept("blocked code action on a read-only document: " + actionTitle);
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

        if (requireConfirmation && !confirmWorkspaceEdit(actionTitle, resolvedEdits)) {
            return;
        }

        try {
            applyEdits(document, resolvedEdits);
            setStatus("sqldeps change applied; use Undo to revert");
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action could not be applied: " + exception.getMessage());
        }
    }

    private void setStatus(String message) {
        editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
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
            boolean existsNow = Files.exists(externalEdit.path());
            boolean expectedToExist = !externalEdit.createIfMissing();
            if (existsNow != expectedToExist) {
                logConsumer.accept("sqldeps external code action target changed; not applied");
                return;
            }

            String currentText = existsNow
                ? Files.readString(externalEdit.path(), StandardCharsets.UTF_8)
                : "";
            if (!currentText.equals(externalEdit.originalText())) {
                logConsumer.accept("sqldeps external code action target changed; not applied");
                return;
            }

            applyEdits(target, externalEdit.edits());
            if (externalEdit.createIfMissing()) {
                Files.writeString(
                    externalEdit.path(),
                    target.get(),
                    StandardCharsets.UTF_8,
                    StandardOpenOption.CREATE_NEW,
                    StandardOpenOption.WRITE
                );
            }

            else {
                Files.writeString(
                    externalEdit.path(),
                    target.get(),
                    StandardCharsets.UTF_8,
                    StandardOpenOption.TRUNCATE_EXISTING,
                    StandardOpenOption.WRITE
                );
            }

            setStatus("sqldeps configuration change applied: " + externalEdit.path());
            refreshDiagnostics.run();
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
