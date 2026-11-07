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
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.dialogs.DialogSettings;
import org.eclipse.jface.dialogs.IDialogSettings;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.jface.window.Window;
import org.eclipse.core.runtime.CoreException;
import org.eclipse.core.runtime.IProgressMonitor;
import org.eclipse.core.runtime.IStatus;
import org.eclipse.core.runtime.Status;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionContext;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DiagnosticSeverity;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.ui.PlatformUI;
import org.eclipse.ui.texteditor.IDocumentProvider;
import org.eclipse.ui.texteditor.IDocumentProviderExtension;
import org.eclipse.ui.dialogs.FilteredItemsSelectionDialog;
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
    public void showAt(LspConnection processManager, int offset, int length) {
        if (!isReady(processManager)) {
            return;
        }

        try {
            Range range = rangeAt(offset, length);
            show(processManager, range, diagnosticsForRange(range));
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action range could not be read: " + exception.getMessage());
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

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                logConsumer.accept("sqldeps code action request failed: " + exception.getMessage());
                return;
            }

            showDialog(snapshot, processManager, actions, diagnostics);
        }));
    }

    private Range selectedRange() throws BadLocationException {
        ISelection selection = editor.getSite().getSelectionProvider().getSelection();
        int offset = 0;
        int length = 0;
        if (selection instanceof ITextSelection textSelection) {
            offset = textSelection.getOffset();
            length = textSelection.getLength();
        }

        int start = Math.max(0, Math.min(offset, document.getLength()));
        int end = Math.max(start, Math.min(start + length, document.getLength()));
        return rangeAt(start, end - start);
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
        List<CodeActionChoice> choices = buildChoices(actions, diagnostics);

        if (choices.isEmpty()) {
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

            if (choices.size() == 1) {
                applyChoice(snapshot, processManager, choices.get(0));
                return;
            }

            CodeActionSelectionDialog dialog = new CodeActionSelectionDialog(
                PlatformUI.getWorkbench().getActiveWorkbenchWindow().getShell(),
                choices
            );
            dialog.setTitle("sqldeps code actions");
            if (dialog.open() == Window.OK
                && dialog.getResult() != null
                && dialog.getResult().length > 0
                && dialog.getResult()[0] instanceof CodeActionChoice choice) {
                applyChoice(snapshot, processManager, choice);
            }
        });
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

    private static final class CodeActionSelectionDialog extends FilteredItemsSelectionDialog {
        private static final IDialogSettings DIALOG_SETTINGS = new DialogSettings("sqldeps.codeActions");

        private final List<CodeActionChoice> choices;

        private CodeActionSelectionDialog(
            Shell shell,
            List<CodeActionChoice> choices
        ) {
            super(shell);
            this.choices = choices;
            setListLabelProvider(new LabelProvider() {
                @Override
                public String getText(Object element) {
                    return ((CodeActionChoice) element).title();
                }
            });
        }

        @Override
        protected Composite createExtendedContentArea(Composite parent) {
            return null;
        }

        @Override
        protected IDialogSettings getDialogSettings() {
            return DIALOG_SETTINGS;
        }

        @Override
        protected IStatus validateItem(Object item) {
            return Status.OK_STATUS;
        }

        @Override
        protected ItemsFilter createFilter() {
            return new ItemsFilter() {
                @Override
                public boolean matchItem(Object item) {
                    return matches(getElementName(item));
                }

                @Override
                public boolean isConsistentItem(Object item) {
                    return true;
                }
            };
        }

        @Override
        protected Comparator<CodeActionChoice> getItemsComparator() {
            return Comparator.comparing(
                CodeActionSelectionDialog.this::getElementName,
                String.CASE_INSENSITIVE_ORDER
            );
        }

        @Override
        protected void fillContentProvider(
            AbstractContentProvider contentProvider,
            ItemsFilter filter,
            IProgressMonitor monitor
        ) throws CoreException {
            for (CodeActionChoice choice : choices) {
                if (filter.matchItem(choice)) {
                    contentProvider.add(choice, filter);
                }
            }
        }

        @Override
        public String getElementName(Object item) {
            return ((CodeActionChoice) item).title();
        }
    }

    private List<CodeActionChoice> buildChoices(
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        List<CodeActionChoice> choices = new ArrayList<>();
        if (actions == null) {
            return choices;
        }

        for (Either<Command, CodeAction> action : actions) {
            if (action.isLeft() && isSupportedCommand(action.getLeft())) {
                Command command = action.getLeft();
                choices.add(new CodeActionChoice(
                    describeCommand(command, diagnostics),
                    null,
                    command
                ));
                continue;
            }

            if (!action.isRight()) {
                continue;
            }

            CodeAction codeAction = action.getRight();
            if (codeAction.getEdit() != null && isDocumentEditable()) {
                choices.add(new CodeActionChoice(
                    describeAction(codeAction.getTitle(), codeAction, diagnostics),
                    codeAction,
                    null
                ));
                continue;
            }

            if (isSupportedCommand(codeAction.getCommand())) {
                choices.add(new CodeActionChoice(
                    describeAction(codeAction.getTitle(), codeAction, diagnostics),
                    null,
                    codeAction.getCommand()
                ));
            }
        }

        return choices;
    }

    private String describeAction(
        String title,
        CodeAction codeAction,
        List<Diagnostic> diagnostics
    ) {
        return describeAction(categoryFor(codeAction), title, codeAction, diagnostics);
    }

    private String describeCommand(Command command, List<Diagnostic> diagnostics) {
        return describeAction(categoryFor(command), command.getTitle(), null, diagnostics);
    }

    private String describeAction(
        String category,
        String title,
        CodeAction codeAction,
        List<Diagnostic> diagnostics
    ) {
        String categorizedTitle = "[" + category + "] " + title + modeDescription(category, codeAction);
        Diagnostic diagnostic = relatedDiagnostic(codeAction, diagnostics);
        if (diagnostic == null) {
            return categorizedTitle
                + evidenceDescription(codeAction)
                + applicabilityDescription(codeAction)
                + explanationDescription(codeAction);
        }

        String severity = severityName(diagnostic.getSeverity());
        String message = diagnostic.getMessage();
        if (message.length() > 160) {
            message = message.substring(0, 157) + "...";
        }

        return categorizedTitle
            + evidenceDescription(codeAction)
            + applicabilityDescription(codeAction)
            + explanationDescription(codeAction)
            + " — [" + severity + "] " + message;
    }

    private String modeDescription(String category, CodeAction codeAction) {
        if (codeAction != null && codeAction.getEdit() != null) {
            return " — Applies edit";
        }

        if ("Preview".equals(category)) {
            return " — Preview only";
        }

        return " — Runs command";
    }

    private String categoryFor(CodeAction codeAction) {
        if (codeAction != null && codeAction.getEdit() != null) {
            if (isExperimental(codeAction)) {
                return "Generate";
            }

            return "Quick Fix";
        }

        return categoryFor(codeAction == null ? null : codeAction.getCommand());
    }

    private String categoryFor(Command command) {
        if (command == null) {
            return "Action";
        }

        return switch (command.getCommand()) {
            case "sqldeps.query-builder", "sqldeps.index-suggestion" -> "Preview";
            case "sqldeps.repository-context" -> "Context";
            case "sqldeps.explain-diagnostic" -> "Review";
            case "sqldeps.reindex" -> "Maintenance";
            default -> "Action";
        };
    }

    private boolean isExperimental(CodeAction codeAction) {
        if (!(codeAction.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        if (Boolean.TRUE.equals(data.get("experimental"))) {
            return true;
        }

        Object generation = data.get("generation");
        return generation instanceof String value && value.contains("rewrite");
    }

    private String evidenceDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        Object referencesValue = data.get("references");
        if (!(referencesValue instanceof List<?> references) || references.isEmpty()) {
            return "";
        }

        String firstReferenceTitle = referenceTitle(references.get(0));
        String description = firstReferenceTitle.isBlank()
            ? references.size() + " source(s)"
            : firstReferenceTitle;
        if (references.size() > 1) {
            description += " +" + (references.size() - 1) + " more";
        }

        return " — Evidence: " + description;
    }

    private String explanationDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        if (!(data.get("explanation") instanceof Map<?, ?> explanation)) {
            return "";
        }

        Object summary = explanation.get("summary");
        if (!(summary instanceof String value) || value.isBlank()) {
            return "";
        }

        String shortened = value.length() > 120
            ? value.substring(0, 117) + "..."
            : value;

        return " — Why: " + shortened;
    }

    private String applicabilityDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        Object applicability = data.get("applicability");
        if (!(applicability instanceof String value) || value.isBlank()) {
            return "";
        }

        String shortened = value.length() > 140
            ? value.substring(0, 137) + "..."
            : value;

        return " — Applies when: " + shortened;
    }

    private static String referenceTitle(Object reference) {
        if (!(reference instanceof Map<?, ?> referenceMap)) {
            return "";
        }

        Object title = referenceMap.get("Title");
        if (title == null) {
            title = referenceMap.get("title");
        }

        return title instanceof String value ? value : "";
    }

    private Diagnostic relatedDiagnostic(CodeAction codeAction, List<Diagnostic> diagnostics) {
        if (codeAction != null && codeAction.getDiagnostics() != null && !codeAction.getDiagnostics().isEmpty()) {
            return codeAction.getDiagnostics().get(0);
        }

        if (diagnostics.size() == 1) {
            return diagnostics.get(0);
        }

        if (codeAction != null && codeAction.getEdit() != null && codeAction.getEdit().getChanges() != null) {
            for (List<TextEdit> edits : codeAction.getEdit().getChanges().values()) {
                if (edits == null) {
                    continue;
                }

                for (TextEdit edit : edits) {
                    if (edit == null || edit.getRange() == null) {
                        continue;
                    }

                    for (Diagnostic diagnostic : diagnostics) {
                        if (sameLineOrOverlapping(edit.getRange(), diagnostic.getRange())) {
                            return diagnostic;
                        }
                    }
                }
            }
        }

        return null;
    }

    private boolean sameLineOrOverlapping(Range first, Range second) {
        if (first.getStart().getLine() == second.getStart().getLine()
            && first.getEnd().getLine() == second.getEnd().getLine()) {
            return true;
        }

        return DiagnosticRangeSelection.overlaps(document, first, second);
    }

    private static String severityName(DiagnosticSeverity severity) {
        if (severity == DiagnosticSeverity.Error) {
            return "error";
        }

        if (severity == DiagnosticSeverity.Warning) {
            return "warning";
        }

        if (severity == DiagnosticSeverity.Information) {
            return "info";
        }

        if (severity == DiagnosticSeverity.Hint) {
            return "hint";
        }

        return "diagnostic";
    }

    private boolean isSupportedCommand(Command command) {
        return command != null && switch (command.getCommand()) {
            case "sqldeps.query-builder", "sqldeps.index-suggestion", "sqldeps.explain-diagnostic", "sqldeps.repository-context", "sqldeps.reindex" -> true;
            default -> false;
        };
    }

    private boolean isPreviewCommand(String command) {
        return "sqldeps.query-builder".equals(command)
            || "sqldeps.index-suggestion".equals(command)
            || "sqldeps.explain-diagnostic".equals(command)
            || "sqldeps.repository-context".equals(command);
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
            logConsumer.accept("sqldeps did not apply the code action to a read-only document");
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
            for (WorkspaceEditSafety.ResolvedTextEdit resolvedEdit : resolvedEdits) {
                document.replace(
                    resolvedEdit.start(),
                    resolvedEdit.end() - resolvedEdit.start(),
                    resolvedEdit.newText()
                );
            }

            editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(
                "sqldeps change applied; use Undo to revert");
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action could not be applied: " + exception.getMessage());
        }
    }

    private boolean confirmWorkspaceEdit(
        String actionTitle,
        List<WorkspaceEditSafety.ResolvedTextEdit> edits
    ) {
        String preview = workspaceEditPreview(edits);
        if (preview == null) {
            logConsumer.accept("sqldeps code action preview could not be created; not applied");

            return false;
        }

        var window = PlatformUI.getWorkbench().getActiveWorkbenchWindow();
        if (window == null || window.getShell() == null || window.getShell().isDisposed()) {
            return false;
        }

        return MessageDialog.openQuestion(
            window.getShell(),
            "Preview: " + actionTitle,
            preview + "\n\nApply this change? The editor Undo command can revert it.");
    }

    private String workspaceEditPreview(List<WorkspaceEditSafety.ResolvedTextEdit> edits) {
        StringBuilder preview = new StringBuilder("The following edit will be applied:\n");

        for (int index = edits.size() - 1; index >= 0; index--) {
            WorkspaceEditSafety.ResolvedTextEdit edit = edits.get(index);

            try {
                String oldText = document.get(edit.start(), edit.end() - edit.start());
                int line = document.getLineOfOffset(edit.start()) + 1;
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

    private record CodeActionChoice(String title, CodeAction action, Command command) {
    }

}

