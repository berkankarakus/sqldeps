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

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CancellationException;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Consumer;

import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspProcessManager;

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
    private volatile LspProcessManager processManager;
    private volatile CompletableFuture<DocumentDiagnosticReport> pendingDiagnosticRequest;
    private final AtomicLong diagnosticsRequestSequence = new AtomicLong();
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
    }

    void start() {
        startProcess();
    }

    void restart() {
        if (closed) {
            return;
        }

        scheduler.execute(() -> {
            closeProcess();
            if (!closed) {
                startProcess();
            }
        });
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

    LspProcessManager processManager() {
        return processManager;
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
        LspProcessManager manager = processManager;
        processManager = null;
        scheduler.shutdownNow();
        CompletableFuture.runAsync(() -> {
            if (manager != null) {
                manager.didClose(documentUri);
                manager.close();
            }
        });
    }

    private void startProcess() {
        try {
            LspProcessManager manager = startLspProcess();
            processManager = manager;
            logConsumer.accept("sqldeps LSP hazırlandı");

            if (closed) {
                manager.close();
                return;
            }

            openDocument(manager);
        }

        catch (Exception exception) {
            logConsumer.accept("sqldeps LSP başlatılamadı: " + exception.getMessage());
        }
    }

    private LspProcessManager startLspProcess() throws Exception {
        return LspProcessManager.start(
            SqlEditorLspAddIn.configuredCommand(),
            workspaceUri,
            IndexSelectionState.selectedIndexPath(),
            IndexSelectionState.automaticIndexSelection(),
            ignored -> {
            },
            logConsumer
        );
    }

    private void openDocument(LspProcessManager manager) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        manager.didOpen(snapshot);
        logConsumer.accept("sqldeps belge gönderildi: " + snapshot.text().length() + " karakter");
        requestDiagnostics(snapshot);
    }

    private void closeProcess() {
        cancelPendingDiagnostics();
        LspProcessManager manager = processManager;
        processManager = null;
        if (manager != null) {
            manager.close();
        }
    }

    private void synchronizeChangedDocument(DocumentSnapshot snapshot) {
        if (!isRunning()) {
            return;
        }

        processManager.didChange(snapshot);
        requestDiagnostics(snapshot);
    }

    private boolean isRunning() {
        return !closed && processManager != null && synchronizer != null;
    }

    private void requestDiagnostics(DocumentSnapshot snapshot) {
        LspProcessManager manager = processManager;
        if (manager == null || diagnosticController == null) {
            return;
        }

        long requestSequence = diagnosticsRequestSequence.incrementAndGet();
        CompletableFuture<DocumentDiagnosticReport> previousRequest = pendingDiagnosticRequest;
        if (previousRequest != null && !previousRequest.isDone()) {
            previousRequest.cancel(true);
        }

        CompletableFuture<DocumentDiagnosticReport> request = manager.requestDiagnostics(snapshot.uri());
        pendingDiagnosticRequest = request;
        request.whenComplete((report, exception) -> {
            if (requestSequence != diagnosticsRequestSequence.get() || closed) {
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

    private static int diagnosticCount(DocumentDiagnosticReport report) {
        if (report == null || report.getRelatedFullDocumentDiagnosticReport() == null) {
            return 0;
        }

        return report.getRelatedFullDocumentDiagnosticReport().getItems().size();
    }

    private void cancelPendingDiagnostics() {
        diagnosticsRequestSequence.incrementAndGet();
        CompletableFuture<DocumentDiagnosticReport> request = pendingDiagnosticRequest;
        pendingDiagnosticRequest = null;
        if (request != null && !request.isDone()) {
            request.cancel(true);
        }
    }
}
