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

import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.Consumer;

import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;
import org.sqldeps.dbeaver.lsp.LspProcessPool;

final class EditorLspSession implements AutoCloseable {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final String documentUri;
    private final String workspaceUri;
    private final ScheduledExecutorService scheduler;
    private final String diagnosticsOperationName;
    private final Consumer<String> logConsumer;

    private DocumentSynchronizer synchronizer;
    private DiagnosticController diagnosticController;
    private LspDocumentLifecycle lifecycle;
    private final DiagnosticRequestGate diagnosticRequestGate = new DiagnosticRequestGate();
    private volatile boolean closed;

    EditorLspSession(
        SQLEditorBase editor,
        IDocument document,
        String documentUri,
        String workspaceUri,
        ScheduledExecutorService scheduler,
        String diagnosticsOperationName,
        Consumer<String> logConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.documentUri = documentUri;
        this.workspaceUri = workspaceUri;
        this.scheduler = scheduler;
        this.diagnosticsOperationName = diagnosticsOperationName;
        this.logConsumer = logConsumer;
    }

    void initialize() {
        synchronizer = new DocumentSynchronizer(
            document,
            documentUri,
            scheduler,
            this::synchronizeChangedDocument
        );
        diagnosticController = new DiagnosticController(
            editor,
            document,
            synchronizer,
            logConsumer
        );
        lifecycle = new LspDocumentLifecycle(
            scheduler,
            documentUri,
            this::startLspProcess,
            this::openDocument,
            exception -> logConsumer.accept("sqldeps LSP başlatılamadı: " + exception.getMessage())
        );
    }

    void start() {
        lifecycle.start();
    }

    void restart() {
        if (closed) {
            return;
        }

        cancelPendingDiagnostics();
        lifecycle.restart();
    }

    void analyze() {
        if (!isRunning()) {
            return;
        }

        scheduler.execute(() -> requestDiagnostics(synchronizer.snapshot()));
    }

    void showDiagnostics() {
        if (diagnosticController != null) {
            diagnosticController.showDiagnostics();
        }
    }

    DocumentSynchronizer synchronizer() {
        return synchronizer;
    }

    DiagnosticController diagnosticController() {
        return diagnosticController;
    }

    LspConnection processManager() {
        return lifecycle == null ? null : lifecycle.connection();
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        if (synchronizer != null) {
            synchronizer.close();
        }

        cancelPendingDiagnostics();
        if (lifecycle != null) {
            lifecycle.close();
        }
    }

    private LspConnection startLspProcess() throws Exception {
        return LspProcessPool.acquire(
            SqlEditorLspAddIn.configuredCommand(),
            workspaceUri,
            IndexSelectionState.selectedIndexPath(),
            IndexSelectionState.automaticIndexSelection(),
            logConsumer
        );
    }

    private void openDocument(LspConnection manager) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        manager.didOpen(snapshot);
        logConsumer.accept("sqldeps LSP hazırlandı");
        logConsumer.accept("sqldeps belge gönderildi: " + snapshot.text().length() + " karakter");
        requestDiagnostics(snapshot);
    }

    private void synchronizeChangedDocument(DocumentSnapshot snapshot) {
        if (!isRunning()) {
            return;
        }

        lifecycle.didChange(snapshot);
        requestDiagnostics(snapshot);
    }

    private void requestDiagnostics(DocumentSnapshot snapshot) {
        LspConnection manager = processManager();
        if (manager == null || diagnosticController == null) {
            return;
        }

        long requestSequence = diagnosticRequestGate.begin();

        CompletableFuture<DocumentDiagnosticReport> request = manager.requestDiagnostics(snapshot.uri());
        diagnosticRequestGate.track(requestSequence, request);
        request.whenComplete((report, exception) -> {
            if (!diagnosticRequestGate.isCurrent(requestSequence) || closed) {
                return;
            }

            if (exception != null) {
                if (exception instanceof CancellationException) {
                    return;
                }

                diagnosticController.logFailure(diagnosticsOperationName, exception);
                return;
            }

            logConsumer.accept("sqldeps diagnostics alındı: " + diagnosticCount(report));
            diagnosticController.apply(snapshot, report);
        });
    }

    private boolean isRunning() {
        return !closed && lifecycle != null && lifecycle.isRunning() && synchronizer != null;
    }

    private static int diagnosticCount(DocumentDiagnosticReport report) {
        if (report == null || report.getRelatedFullDocumentDiagnosticReport() == null) {
            return 0;
        }

        return report.getRelatedFullDocumentDiagnosticReport().getItems().size();
    }

    private void cancelPendingDiagnostics() {
        diagnosticRequestGate.close();
    }
}
