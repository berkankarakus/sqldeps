package org.sqldeps.dbeaver.lsp;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import org.eclipse.lsp4j.DocumentDiagnosticReport;

public final class LspProcessBaseline {
    private LspProcessBaseline() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2 || args.length > 5) {
            throw new IllegalArgumentException(
                "Expected: <sqldeps-executable> <process-count> [index-path] [automatic-index-selection] [shared-process]"
            );
        }

        Path executable = Path.of(args[0]);
        int processCount = Integer.parseInt(args[1]);
        String indexPath = args.length >= 3 && !args[2].isBlank() ? args[2] : null;
        boolean automaticIndexSelection = args.length < 4 || Boolean.parseBoolean(args[3]);
        boolean sharedProcess = args.length < 5 || Boolean.parseBoolean(args[4]);

        if (processCount < 1) {
            throw new IllegalArgumentException("Process count must be positive.");
        }

        measure(executable, processCount, indexPath, automaticIndexSelection, sharedProcess);
    }

    private static void measure(
        Path executable,
        int processCount,
        String indexPath,
        boolean automaticIndexSelection,
        boolean sharedProcess
    ) throws Exception {
        List<LspConnection> connections = new ArrayList<>(processCount);
        long startupStarted = System.nanoTime();

        try {
            for (int index = 0; index < processCount; index++) {
                connections.add(start(
                    executable,
                    indexPath,
                    automaticIndexSelection,
                    sharedProcess
                ));
            }

            long startupElapsed = System.nanoTime() - startupStarted;
            List<CompletableFuture<DocumentDiagnosticReport>> requests = openDocuments(connections);
            long diagnosticsStarted = System.nanoTime();
            CompletableFuture.allOf(requests.toArray(CompletableFuture[]::new))
                .get(30, TimeUnit.SECONDS);
            long diagnosticsElapsed = System.nanoTime() - diagnosticsStarted;

            System.out.printf(
                "mode=%s processes=%d active_processes=%d startup_ms=%.1f startup_avg_ms=%.1f diagnostics_ms=%.1f diagnostics_avg_ms=%.1f%n",
                sharedProcess ? "shared" : "independent",
                processCount,
                sharedProcess ? LspProcessPool.activeProcessCount() : processCount,
                milliseconds(startupElapsed),
                milliseconds(startupElapsed) / processCount,
                milliseconds(diagnosticsElapsed),
                milliseconds(diagnosticsElapsed) / processCount
            );
        }

        finally {
            for (LspConnection connection : connections) {
                connection.close();
            }
        }
    }

    private static LspConnection start(
        Path executable,
        String indexPath,
        boolean automaticIndexSelection,
        boolean sharedProcess
    ) throws Exception {
        if (sharedProcess) {
            return LspProcessPool.acquire(
                List.of(executable.toString(), "lsp"),
                null,
                indexPath,
                automaticIndexSelection,
                ignored -> {
                }
            );
        }

        return LspProcessManager.start(
            List.of(executable.toString(), "lsp"),
            null,
            indexPath,
            automaticIndexSelection,
            ignored -> {
            },
            ignored -> {
            }
        );
    }

    private static List<CompletableFuture<DocumentDiagnosticReport>> openDocuments(
        List<LspConnection> connections)
    {
        List<CompletableFuture<DocumentDiagnosticReport>> requests = new ArrayList<>(connections.size());

        for (int index = 0; index < connections.size(); index++) {
            DocumentSnapshot snapshot = new DocumentSnapshot(
                "file:///tmp/sqldeps-baseline-" + index + ".sql",
                1,
                1,
                "SELECT 1;"
            );
            LspConnection connection = connections.get(index);
            connection.didOpen(snapshot);
            requests.add(connection.requestDiagnostics(snapshot.uri()));
        }

        return requests;
    }

    private static double milliseconds(long nanoseconds) {
        return nanoseconds / 1_000_000.0;
    }
}
