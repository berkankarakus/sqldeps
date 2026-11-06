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
import java.util.function.Consumer;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.source.IAnnotationModel;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.swt.widgets.Display;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;

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
    public void markAnalyzing() {
        updateStatus("sqldeps: analyzing...");
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
            logConsumer.accept("sqldeps annotation modeli hazır değil; tanılar editöre uygulanamadı");
            updateStatus("sqldeps: annotation model unavailable");
            return;
        }

        diagnosticManager.replace(model, document, diagnostics);
        refreshEditorPresentation();
        updateStatus(diagnostics.isEmpty()
            ? "sqldeps: up to date — no findings"
            : "sqldeps: up to date — " + diagnostics.size() + " finding(s)");
    }

    private void refreshEditorPresentation() {
        if (editorPresentationRefreshed) {
            return;
        }

        editor.reloadSyntaxRules();
        editorPresentationRefreshed = true;
    }

    /** Records a request failure without interrupting the editor UI thread. */
    public void logFailure(String operation, Throwable exception) {
        logConsumer.accept("sqldeps " + operation + " isteği başarısız: " + exception.getMessage());
        updateStatus("sqldeps: analysis failed");
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
        reveal(diagnosticSelection.first());
    }

    /** Moves to the next diagnostic, wrapping at the end of the list. */
    public void showNextDiagnostic() {
        reveal(diagnosticSelection.next());
    }

    /** Moves to the previous diagnostic, wrapping at the beginning of the list. */
    public void showPreviousDiagnostic() {
        reveal(diagnosticSelection.previous());
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
            logConsumer.accept("sqldeps diagnostic konumu editörde gösterilemedi: " + exception.getMessage());
        }
    }

}

