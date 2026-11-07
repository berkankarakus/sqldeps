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
package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.source.IAnnotationModel;
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.swt.widgets.Display;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;
import org.sqldeps.dbeaver.editor.index.IndexStatus;

/** Applies LSP diagnostics to DBeaver annotations and navigates between findings. */
public final class DiagnosticController {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final DiagnosticManager diagnosticManager = new DiagnosticManager();
    private final Consumer<String> logConsumer;

    private final DiagnosticSelection diagnosticSelection = new DiagnosticSelection();
    private boolean editorPresentationRefreshed;

    /** Creates a controller for one SQL editor and its synchronized document. */
    public DiagnosticController(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        Consumer<String> logConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.logConsumer = logConsumer;
    }

    /** Returns the latest diagnostics accepted for the current document generation. */
    public List<Diagnostic> currentDiagnostics() {
        return diagnosticSelection.current();
    }

    /** Shows that a diagnostic request is in flight for the current editor document. */
    public void markStarting() {
        updateStatus(statusMessage("starting..."));
    }

    /** Shows that the editor is restarting its LSP session. */
    public void markRestarting() {
        updateStatus(statusMessage("restarting..."));
    }

    /** Shows that the LSP session is ready for requests. */
    public void markReady() {
        updateStatus(statusMessage("ready"));
    }

    /** Shows that the index refresh command is running. */
    public void markReindexing() {
        updateStatus(statusMessage("reindexing..."));
    }

    /** Shows a failed LSP lifecycle transition. */
    public void markFailed() {
        updateStatus(statusMessage("failed"));
    }

    /** Shows that a diagnostic request is in flight for the current editor document. */
    public void markAnalyzing() {
        updateStatus(statusMessage("analyzing..."));
    }

    /** Applies a pull-diagnostic response only when its snapshot is still current. */
    public void apply(DocumentSnapshot snapshot, DocumentDiagnosticReport report) {
        List<Diagnostic> diagnostics = diagnosticsFrom(report);

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> applyCurrentSnapshot(snapshot, diagnostics));
    }

    private static List<Diagnostic> diagnosticsFrom(DocumentDiagnosticReport report) {
        if (report == null || report.getRelatedFullDocumentDiagnosticReport() == null) {
            return List.of();
        }

        return report.getRelatedFullDocumentDiagnosticReport().getItems();
    }

    private void applyCurrentSnapshot(DocumentSnapshot snapshot, List<Diagnostic> diagnostics) {
        if (!synchronizer.isCurrent(snapshot)) {
            return;
        }

        diagnosticSelection.replace(diagnostics);
        IAnnotationModel model = editor.getAnnotationModel();
        if (model == null) {
            logConsumer.accept("sqldeps annotation model is not ready; diagnostics were not applied");
            updateStatus("sqldeps: annotation model unavailable");
            return;
        }

        diagnosticManager.replace(model, document, diagnostics);
        refreshEditorPresentation();
        String result = resultStatus(diagnostics);
        updateStatus(statusMessage(result, diagnostics));
    }

    private void refreshEditorPresentation() {
        if (editorPresentationRefreshed) {
            return;
        }

        editor.reloadSyntaxRules();
        editorPresentationRefreshed = true;
    }

    private static String resultStatus(List<Diagnostic> diagnostics) {
        if (hasStaleIndex(diagnostics)) {
            return "stale index — " + diagnostics.size() + " finding(s)";
        }

        if (hasPartialAnalysis(diagnostics)) {
            return "partial analysis — " + diagnostics.size() + " finding(s)";
        }

        if (diagnostics.isEmpty()) {
            return "up to date — no findings";
        }

        return "up to date — " + diagnostics.size() + " finding(s)";
    }

    private static boolean hasStaleIndex(List<Diagnostic> diagnostics) {
        return diagnostics.stream().anyMatch(DiagnosticController::isStaleIndexDiagnostic);
    }

    private static boolean hasPartialAnalysis(List<Diagnostic> diagnostics) {
        return diagnostics.stream().anyMatch(DiagnosticController::isPartialAnalysisDiagnostic);
    }

    private static boolean isPartialAnalysisDiagnostic(Diagnostic diagnostic) {
        Either<String, Integer> code = diagnostic.getCode();

        return code != null && code.isLeft() && "partial-analysis".equals(code.getLeft());
    }

    private static boolean isStaleIndexDiagnostic(Diagnostic diagnostic) {
        if ("stale-index".equals(diagnostic.getSource())) {
            return true;
        }

        if (!(diagnostic.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        return Boolean.TRUE.equals(data.get("stale"))
            || "stale-index".equals(data.get("source"));
    }

    /** Records a request failure without interrupting the editor UI thread. */
    public void logFailure(String operation, Throwable exception) {
        logConsumer.accept("sqldeps " + operation + " request failed: " + exception.getMessage());
        markFailed();
    }

    private static String statusMessage(String result) {
        return statusMessage(result, List.of());
    }

    private static String statusMessage(String result, List<Diagnostic> diagnostics) {
        String message = "sqldeps: " + result + " — " + IndexStatus.describe();

        if (hasStaleIndex(diagnostics)) {
            return message + " · run Reindex sqldeps index";
        }

        if (IndexStatus.requiresSelection()) {
            return message + " · select a sqldeps index";
        }

        return message;
    }

    private void updateStatus(String message) {
        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> {
            if (editor.isDisposed() || editor.getEditorSite() == null) {
                return;
            }

            editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
        });
    }

    /** Reveals the first currently accepted diagnostic. */
    public void showDiagnostics() {
        List<Diagnostic> diagnostics = currentDiagnostics();
        if (diagnostics.isEmpty()) {
            updateStatus("sqldeps: no findings — " + IndexStatus.describe());

            return;
        }

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> {
            if (editor.isDisposed() || editor.getEditorSite() == null) {
                return;
            }

            MessageDialog.openInformation(
                editor.getEditorSite().getShell(),
                "sqldeps Diagnostics",
                diagnosticsSummary(diagnostics));
        });
    }

    /** Moves to the next diagnostic, wrapping at the end of the list. */
    public void showNextDiagnostic() {
        reveal(diagnosticSelection.next());
    }

    /** Moves to the previous diagnostic, wrapping at the beginning of the list. */
    public void showPreviousDiagnostic() {
        reveal(diagnosticSelection.previous());
    }

    private static String diagnosticsSummary(List<Diagnostic> diagnostics) {
        StringBuilder summary = new StringBuilder();
        summary.append(diagnostics.size()).append(" finding(s)\n\n");

        int displayed = 0;
        for (Diagnostic diagnostic : diagnostics) {
            if (displayed == 30) {
                summary.append("\n...").append(diagnostics.size() - displayed).append(" more");

                break;
            }

            summary.append(displayed + 1)
                .append(". [")
                .append(DiagnosticSeverityName.of(diagnostic.getSeverity()))
                .append("] ")
                .append(diagnosticCode(diagnostic))
                .append(" — ")
                .append(diagnostic.getMessage())
                .append('\n');
            displayed++;
        }

        return summary.toString();
    }

    private static String diagnosticCode(Diagnostic diagnostic) {
        Either<String, Integer> code = diagnostic.getCode();
        if (code == null) {
            return "diagnostic";
        }

        if (code.isLeft()) {
            return code.getLeft();
        }

        return Integer.toString(code.getRight());
    }

    private void reveal(Diagnostic diagnostic) {
        if (diagnostic == null) {
            return;
        }

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> revealDiagnostic(diagnostic));
    }

    private void revealDiagnostic(Diagnostic diagnostic) {
        try {
            int start = Utf16DocumentPosition.toOffset(document, diagnostic.getRange().getStart());
            int end = Utf16DocumentPosition.toOffset(document, diagnostic.getRange().getEnd());
            editor.selectAndReveal(start, Math.max(0, end - start));
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps diagnostic location could not be shown in the editor: " + exception.getMessage());
        }
    }

}

