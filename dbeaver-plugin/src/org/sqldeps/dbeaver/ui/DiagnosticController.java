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

public final class DiagnosticController {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final DiagnosticManager diagnosticManager = new DiagnosticManager();
    private final Consumer<String> logConsumer;

    private volatile List<Diagnostic> currentDiagnostics = List.of();
    private int currentDiagnosticIndex = -1;
    private boolean editorPresentationRefreshed;

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

    public List<Diagnostic> currentDiagnostics() {
        return currentDiagnostics;
    }

    public void apply(DocumentSnapshot snapshot, DocumentDiagnosticReport report) {
        List<Diagnostic> diagnostics = report == null || report.getRelatedFullDocumentDiagnosticReport() == null
            ? List.of()
            : report.getRelatedFullDocumentDiagnosticReport().getItems();

        Display display = Display.getDefault();
        if (display.isDisposed()) {
            return;
        }

        display.asyncExec(() -> {
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
            if (!editorPresentationRefreshed) {
                editor.reloadSyntaxRules();
                editorPresentationRefreshed = true;
            }
        });
    }

    public void logFailure(String operation, Throwable exception) {
        logConsumer.accept("sqldeps " + operation + " isteği başarısız: " + exception.getMessage());
    }

    public void showDiagnostics() {
        revealDiagnosticAt(0);
    }

    public void showNextDiagnostic() {
        revealRelativeDiagnostic(1);
    }

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

