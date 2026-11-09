package org.sqldeps.dbeaver.editor;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;

public final class LspDocumentLifecycleTest {
    private LspDocumentLifecycleTest() {
    }

    public static void main(String[] args) throws Exception {
        restartClosesPreviousConnection();

        System.out.println("sqldeps: LSP document lifecycle tests ok");
    }

    private static void restartClosesPreviousConnection() throws Exception {
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        List<FakeConnection> connections = new ArrayList<>();
        CountDownLatch opened = new CountDownLatch(2);
        CountDownLatch closed = new CountDownLatch(1);
        DocumentSnapshot snapshot = new DocumentSnapshot(
            "file:///tmp/sqldeps-lifecycle.sql",
            1,
            1,
            "SELECT 1;"
        );
        LspDocumentLifecycle lifecycle = new LspDocumentLifecycle(
            scheduler,
            snapshot.uri(),
            () -> {
                CountDownLatch closeLatch = connections.isEmpty() ? new CountDownLatch(0) : closed;
                FakeConnection connection = new FakeConnection(closeLatch);
                connections.add(connection);
                return connection;
            },
            connection -> {
                connection.didOpen(snapshot);
                opened.countDown();
            },
            exception -> {
                throw new AssertionError("Unexpected connection failure", exception);
            }
        );

        try {
            lifecycle.start();
            lifecycle.restart();

            check(opened.await(5, TimeUnit.SECONDS), "both connections opened");
            check(connections.size() == 2, "restart creates a new connection");
            check(connections.get(0).didOpenCount == 1, "first connection opened document");
            check(connections.get(0).didCloseCount == 1, "restart closes previous document");
            check(connections.get(0).closeCount == 1, "restart closes previous connection");
            check(lifecycle.isRunning(), "lifecycle remains running after restart");

            lifecycle.close();
            check(closed.await(5, TimeUnit.SECONDS), "close completes asynchronously");
            check(connections.get(1).didCloseCount == 1, "close closes current document");
            check(connections.get(1).closeCount == 1, "close closes current connection");

            lifecycle.close();
            check(connections.get(1).closeCount == 1, "close is idempotent");
        }

        finally {
            lifecycle.close();
            scheduler.shutdownNow();
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }

    private static final class FakeConnection implements LspConnection {
        private final CountDownLatch closed;
        private int didOpenCount;
        private int didCloseCount;
        private int closeCount;

        private FakeConnection(CountDownLatch closed) {
            this.closed = closed;
        }

        @Override
        public void didOpen(DocumentSnapshot snapshot) {
            didOpenCount++;
        }

        @Override
        public void didChange(DocumentChange change) {
        }

        @Override
        public void didClose(String uri) {
            didCloseCount++;
        }

        @Override
        public CompletableFuture<DocumentDiagnosticReport> requestDiagnostics(String uri) {
            return CompletableFuture.completedFuture(null);
        }

        @Override
        public CompletableFuture<List<Either<Command, CodeAction>>> requestCodeActions(CodeActionParams params) {
            return CompletableFuture.completedFuture(List.of());
        }

        @Override
        public CompletableFuture<List<? extends TextEdit>> requestFormatting(String uri) {
            return CompletableFuture.completedFuture(List.of());
        }

        @Override
        public CompletableFuture<Object> executeCommand(Command command) {
            return CompletableFuture.completedFuture(null);
        }

        @Override
        public void close() {
            closeCount++;
            closed.countDown();
        }
    }
}
