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

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.jface.window.Window;
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
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.ui.PlatformUI;
import org.eclipse.ui.texteditor.IDocumentProvider;
import org.eclipse.ui.texteditor.IDocumentProviderExtension;
import org.eclipse.ui.dialogs.ElementListSelectionDialog;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;

/** Requests, filters, previews, and applies safe active-document code actions. */
public final class CodeActionController {
    private static final int MAX_WORKSPACE_EDIT_COUNT = 1_000;
    private static final int MAX_WORKSPACE_EDIT_TEXT_CHARACTERS = 1_000_000;

    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final ScheduledExecutorService scheduler;
    private final Supplier<List<Diagnostic>> diagnosticsSupplier;
    private final BooleanSupplier activeSupplier;
    private final Consumer<String> logConsumer;

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

    public void show(LspConnection processManager) {
        if (!isReady(processManager)) {
            return;
        }

        try {
            show(processManager, selectedRange(), diagnosticsSupplier.get());
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action aralığı okunamadı: " + exception.getMessage());
        }
    }

    public void showAt(LspConnection processManager, int offset, int length) {
        if (!isReady(processManager)) {
            return;
        }

        try {
            Range range = rangeAt(offset, length);
            show(processManager, range, diagnosticsForRange(range));
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action aralığı okunamadı: " + exception.getMessage());
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
                logConsumer.accept("sqldeps code action isteği başarısız: " + exception.getMessage());
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
        int rangeStart = toOffset(range.getStart());
        int rangeEnd = Math.max(rangeStart + 1, toOffset(range.getEnd()));

        return diagnosticsSupplier.get().stream()
            .filter(diagnostic -> overlaps(rangeStart, rangeEnd, diagnostic.getRange()))
            .toList();
    }

    private int toOffset(Position position) throws BadLocationException {
        return Utf16DocumentPosition.toOffset(document, position);
    }

    private boolean overlaps(int rangeStart, int rangeEnd, Range diagnosticRange) {
        try {
            int diagnosticStart = toOffset(diagnosticRange.getStart());
            int diagnosticEnd = Math.max(diagnosticStart + 1, toOffset(diagnosticRange.getEnd()));
            return diagnosticStart < rangeEnd && rangeStart < diagnosticEnd;
        }

        catch (BadLocationException exception) {
            return false;
        }
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

            ElementListSelectionDialog dialog = new ElementListSelectionDialog(
                PlatformUI.getWorkbench().getActiveWorkbenchWindow().getShell(),
                new LabelProvider() {
                    @Override
                    public String getText(Object element) {
                        return ((CodeActionChoice) element).title();
                    }
                }
            );
            dialog.setTitle("sqldeps code actions");
            dialog.setElements(choices.toArray());
            if (dialog.open() == Window.OK && dialog.getFirstResult() instanceof CodeActionChoice choice) {
                if (choice.action() != null) {
                    applyWorkspaceEdit(snapshot, choice.action().getEdit());
                }
                else {
                    executeCommand(snapshot, processManager, choice.command());
                }
            }
        });
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
                    describeAction(command.getTitle(), null, diagnostics),
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
        Diagnostic diagnostic = relatedDiagnostic(codeAction, diagnostics);
        if (diagnostic == null) {
            return title;
        }

        String severity = severityName(diagnostic.getSeverity());
        String message = diagnostic.getMessage();
        if (message.length() > 160) {
            message = message.substring(0, 157) + "...";
        }

        return title + " — [" + severity + "] " + message;
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

        try {
            int firstStart = toOffset(first.getStart());
            int firstEnd = Math.max(firstStart + 1, toOffset(first.getEnd()));
            return overlaps(firstStart, firstEnd, second);
        }

        catch (BadLocationException exception) {
            return false;
        }
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
            case "sqldeps.query-builder", "sqldeps.index-suggestion", "sqldeps.explain-diagnostic", "sqldeps.reindex" -> true;
            default -> false;
        };
    }

    private boolean isPreviewCommand(String command) {
        return "sqldeps.query-builder".equals(command)
            || "sqldeps.index-suggestion".equals(command)
            || "sqldeps.explain-diagnostic".equals(command);
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
            logConsumer.accept("sqldeps desteklenmeyen veya güncel olmayan code action komutunu reddetti");
            return;
        }

        processManager.executeCommand(command).whenComplete((result, exception) -> {
            if (exception != null) {
                logConsumer.accept("sqldeps code action komutu başarısız: " + exception.getMessage());
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

    private void applyWorkspaceEdit(DocumentSnapshot snapshot, WorkspaceEdit edit) {
        if (!isDocumentEditable()) {
            logConsumer.accept("sqldeps code action salt-okunur belgeye uygulanmadı");
            return;
        }

        if (!isApplicableWorkspaceEdit(snapshot, edit)) {
            logConsumer.accept("sqldeps code action değişikliği güncel belgeyle eşleşmiyor; uygulanmadı");
            return;
        }

        List<TextEdit> textEdits = edit.getChanges().get(snapshot.uri());
        if (hasTooManyEdits(textEdits)) {
            logConsumer.accept("sqldeps code action düzenleme sayısı güvenli değil; uygulanmadı");
            return;
        }

        List<ResolvedTextEdit> resolvedEdits = new ArrayList<>();
        long totalNewTextCharacters = 0;
        try {
            for (TextEdit textEdit : textEdits) {
                if (isUnsafeTextEdit(textEdit, totalNewTextCharacters)) {
                    logConsumer.accept("sqldeps code action aralığı veya metin boyutu güvenli değil; uygulanmadı");
                    return;
                }

                int start = Utf16DocumentPosition.toOffset(document, textEdit.getRange().getStart());
                int end = Utf16DocumentPosition.toOffset(document, textEdit.getRange().getEnd());
                if (end < start) {
                    logConsumer.accept("sqldeps code action aralığı veya metin boyutu güvenli değil; uygulanmadı");
                    return;
                }

                totalNewTextCharacters += textEdit.getNewText().length();
                resolvedEdits.add(new ResolvedTextEdit(start, end, textEdit.getNewText()));
            }
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action aralığı belgeye çevrilemedi: " + exception.getMessage());
            return;
        }

        resolvedEdits.sort((left, right) -> Integer.compare(right.start(), left.start()));
        if (hasOverlappingEdits(resolvedEdits)) {
            logConsumer.accept("sqldeps code action düzenlemeleri çakışıyor; uygulanmadı");
            return;
        }

        try {
            for (ResolvedTextEdit resolvedEdit : resolvedEdits) {
                document.replace(
                    resolvedEdit.start(),
                    resolvedEdit.end() - resolvedEdit.start(),
                    resolvedEdit.newText()
                );
            }
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action uygulanamadı: " + exception.getMessage());
        }
    }

    private boolean isApplicableWorkspaceEdit(DocumentSnapshot snapshot, WorkspaceEdit edit) {
        return synchronizer.isCurrent(snapshot)
            && edit != null
            && edit.getChanges() != null
            && edit.getChanges().size() == 1
            && edit.getChanges().containsKey(snapshot.uri())
            && (edit.getDocumentChanges() == null || edit.getDocumentChanges().isEmpty());
    }

    private static boolean hasTooManyEdits(List<TextEdit> textEdits) {
        return textEdits == null || textEdits.size() > MAX_WORKSPACE_EDIT_COUNT;
    }

    private boolean isUnsafeTextEdit(TextEdit textEdit, long totalNewTextCharacters) {
        if (textEdit == null) {
            return true;
        }

        if (hasInvalidRange(textEdit.getRange())) {
            return true;
        }

        String newText = textEdit.getNewText();
        if (newText == null) {
            return true;
        }

        int textLength = newText.length();

        return textLength > MAX_WORKSPACE_EDIT_TEXT_CHARACTERS
            || totalNewTextCharacters + textLength > MAX_WORKSPACE_EDIT_TEXT_CHARACTERS;
    }

    private boolean hasInvalidRange(Range range) {
        if (range == null || range.getStart() == null || range.getEnd() == null) {
            return true;
        }

        return !isValidPosition(range.getStart()) || !isValidPosition(range.getEnd());
    }

    private static boolean hasOverlappingEdits(List<ResolvedTextEdit> edits) {
        for (int index = 1; index < edits.size(); index++) {
            ResolvedTextEdit later = edits.get(index - 1);
            ResolvedTextEdit earlier = edits.get(index);

            if (later.start() < earlier.end()) {
                return true;
            }
        }

        return false;
    }

    private boolean isValidPosition(Position position) {
        if (position.getLine() < 0 || position.getLine() >= document.getNumberOfLines()) {
            return false;
        }

        if (position.getCharacter() < 0) {
            return false;
        }

        try {
            return position.getCharacter() <= document.getLineLength(position.getLine());
        }

        catch (BadLocationException exception) {
            return false;
        }
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

    private record ResolvedTextEdit(int start, int end, String newText) {
    }
}

