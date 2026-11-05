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

/** Applies LSP diagnostics to DBeaver annotations and navigates between findings. */
public final class DiagnosticController {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final DiagnosticManager diagnosticManager = new DiagnosticManager();
    private final Consumer<String> logConsumer;

    private volatile List<Diagnostic> currentDiagnostics = List.of();
    private int currentDiagnosticIndex = -1;
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
        return currentDiagnostics;
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

        currentDiagnostics = List.copyOf(diagnostics);
        currentDiagnosticIndex = -1;
        IAnnotationModel model = editor.getAnnotationModel();
        if (model == null) {
            logConsumer.accept("sqldeps annotation modeli hazır değil; tanılar editöre uygulanamadı");
            return;
        }

        diagnosticManager.replace(model, document, diagnostics);
        refreshEditorPresentation();
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
    }

    /** Reveals the first currently accepted diagnostic. */
    public void showDiagnostics() {
        revealDiagnosticAt(0);
    }

    /** Moves to the next diagnostic, wrapping at the end of the list. */
    public void showNextDiagnostic() {
        revealRelativeDiagnostic(1);
    }

    /** Moves to the previous diagnostic, wrapping at the beginning of the list. */
    public void showPreviousDiagnostic() {
        revealRelativeDiagnostic(-1);
    }

    private void revealRelativeDiagnostic(int direction) {
        List<Diagnostic> diagnostics = currentDiagnostics;

        if (diagnostics.isEmpty()) {
            return;
        }

        int targetIndex = currentDiagnosticIndex < 0
            ? direction > 0 ? 0 : diagnostics.size() - 1
            : Math.floorMod(currentDiagnosticIndex + direction, diagnostics.size());
        revealDiagnosticAt(targetIndex);
    }

    private void revealDiagnosticAt(int index) {
        List<Diagnostic> diagnostics = currentDiagnostics;
        if (index < 0 || index >= diagnostics.size()) {
            return;
        }

        currentDiagnosticIndex = index;
        Diagnostic diagnostic = diagnostics.get(index);
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

