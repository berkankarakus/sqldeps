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
package org.sqldeps.dbeaver.editor;

import java.util.List;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.Consumer;

import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;
import org.sqldeps.dbeaver.lsp.LspProcessPool;
import org.sqldeps.dbeaver.editor.actions.ExperimentalSuggestionsState;
import org.sqldeps.dbeaver.editor.diagnostics.DiagnosticController;
import org.sqldeps.dbeaver.editor.index.IndexSelectionState;

/** Coordinates one editor document with its shared LSP connection and diagnostics. */
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
            this::handleStartFailure
        );
    }

    void start() {
        diagnosticController.markStarting();
        lifecycle.start();
    }

    void restart() {
        if (closed) {
            return;
        }

        cancelPendingDiagnostics();
        diagnosticController.markRestarting();
        lifecycle.restart();
    }

    void reindex() {
        if (!isRunning()) {
            return;
        }

        LspConnection manager = processManager();
        if (manager == null) {
            return;
        }

        diagnosticController.markReindexing();
        Command command = new Command(
            "Reindex sqldeps index",
            "sqldeps.reindex",
            List.of()
        );
        manager.executeCommand(command).whenComplete((result, exception) -> {
            if (exception != null) {
                diagnosticController.logFailure("reindex", exception);
                return;
            }

            logConsumer.accept("sqldeps indeks yenilendi");
            restart();
        });
    }

    CompletableFuture<String> repositoryRoot() {
        if (!isRunning()) {
            return CompletableFuture.completedFuture(null);
        }

        LspConnection manager = processManager();
        if (manager == null) {
            return CompletableFuture.completedFuture(null);
        }

        Command command = new Command(
            "Get sqldeps repository root",
            "sqldeps.repository-root",
            List.of()
        );
        return manager.executeCommand(command).thenApply(result -> {
            if (result instanceof String root && !root.isBlank()) {
                return root;
            }

            return null;
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
            IndexSelectionState.indexPathForLsp(),
            IndexSelectionState.automaticIndexSelection(),
            ExperimentalSuggestionsState.isEnabled(),
            logConsumer
        );
    }

    private void openDocument(LspConnection manager) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        manager.didOpen(snapshot);
        diagnosticController.markReady();
        logConsumer.accept("sqldeps LSP is ready");
        logConsumer.accept("sqldeps document sent: " + snapshot.text().length() + " characters");
        requestDiagnostics(snapshot);
    }

    private void synchronizeChangedDocument(DocumentChange change) {
        if (!isRunning()) {
            return;
        }

        lifecycle.didChange(change);
        requestDiagnostics(change.snapshot());
    }

    private void requestDiagnostics(DocumentSnapshot snapshot) {
        LspConnection manager = processManager();
        if (manager == null || diagnosticController == null) {
            return;
        }

        long requestSequence = diagnosticRequestGate.begin();
        diagnosticController.markAnalyzing();
        if (Boolean.getBoolean("sqldeps.lsp.trace")) {
            logConsumer.accept("sqldeps trace: requesting diagnostics for " + snapshot.uri());
        }

        CompletableFuture<DocumentDiagnosticReport> request = manager.requestDiagnostics(snapshot.uri());
        diagnosticRequestGate.track(requestSequence, request);
        request.whenComplete((report, exception) -> handleDiagnosticResponse(
            requestSequence,
            snapshot,
            report,
            exception));
    }

    private void handleDiagnosticResponse(
        long requestSequence,
        DocumentSnapshot snapshot,
        DocumentDiagnosticReport report,
        Throwable exception
    ) {
        if (!isCurrentDiagnosticRequest(requestSequence)) {
            return;
        }

        if (exception != null) {
            handleDiagnosticFailure(exception);
            return;
        }

        logConsumer.accept("sqldeps diagnostics received: " + diagnosticCount(report));
        diagnosticController.apply(snapshot, report);
    }

    private boolean isCurrentDiagnosticRequest(long requestSequence) {
        return !closed && diagnosticRequestGate.isCurrent(requestSequence);
    }

    private void handleDiagnosticFailure(Throwable exception) {
        if (exception instanceof CancellationException) {
            return;
        }

        diagnosticController.logFailure(diagnosticsOperationName, exception);
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

    private void handleStartFailure(Exception exception) {
        diagnosticController.markFailed();
        logConsumer.accept("sqldeps LSP could not be started: " + exception.getMessage());
    }
}
