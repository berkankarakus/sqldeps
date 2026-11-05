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
        if (args.length < 2 || args.length > 4) {
            throw new IllegalArgumentException(
                "Expected: <sqldeps-executable> <process-count> [index-path] [automatic-index-selection]"
            );
        }

        Path executable = Path.of(args[0]);
        int processCount = Integer.parseInt(args[1]);
        String indexPath = args.length >= 3 && !args[2].isBlank() ? args[2] : null;
        boolean automaticIndexSelection = args.length < 4 || Boolean.parseBoolean(args[3]);

        measure(executable, processCount, indexPath, automaticIndexSelection);
    }

    private static void measure(
        Path executable,
        int processCount,
        String indexPath,
        boolean automaticIndexSelection
    ) throws Exception {
        List<LspProcessManager> managers = new ArrayList<>(processCount);
        long startupStarted = System.nanoTime();

        try {
            for (int index = 0; index < processCount; index++) {
                managers.add(start(executable, indexPath, automaticIndexSelection));
            }

            long startupElapsed = System.nanoTime() - startupStarted;
            List<CompletableFuture<DocumentDiagnosticReport>> requests = openDocuments(managers);
            long diagnosticsStarted = System.nanoTime();
            CompletableFuture.allOf(requests.toArray(CompletableFuture[]::new))
                .get(30, TimeUnit.SECONDS);
            long diagnosticsElapsed = System.nanoTime() - diagnosticsStarted;

            System.out.printf(
                "processes=%d startup_ms=%.1f startup_avg_ms=%.1f diagnostics_ms=%.1f diagnostics_avg_ms=%.1f%n",
                processCount,
                milliseconds(startupElapsed),
                milliseconds(startupElapsed) / processCount,
                milliseconds(diagnosticsElapsed),
                milliseconds(diagnosticsElapsed) / processCount
            );
        }

        finally {
            for (LspProcessManager manager : managers) {
                manager.close();
            }
        }
    }

    private static LspProcessManager start(
        Path executable,
        String indexPath,
        boolean automaticIndexSelection
    ) throws Exception {
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
        List<LspProcessManager> managers)
    {
        List<CompletableFuture<DocumentDiagnosticReport>> requests = new ArrayList<>(managers.size());

        for (int index = 0; index < managers.size(); index++) {
            DocumentSnapshot snapshot = new DocumentSnapshot(
                "file:///tmp/sqldeps-baseline-" + index + ".sql",
                1,
                1,
                "SELECT 1;"
            );
            LspProcessManager manager = managers.get(index);
            manager.didOpen(snapshot);
            requests.add(manager.requestDiagnostics(snapshot.uri()));
        }

        return requests;
    }

    private static double milliseconds(long nanoseconds) {
        return nanoseconds / 1_000_000.0;
    }
}
