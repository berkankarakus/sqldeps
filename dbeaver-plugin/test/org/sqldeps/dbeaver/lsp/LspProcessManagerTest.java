package org.sqldeps.dbeaver.lsp;

import java.nio.file.Path;
import java.time.Duration;
import java.util.List;
import java.util.concurrent.TimeUnit;

import org.eclipse.lsp4j.DocumentDiagnosticReport;

public final class LspProcessManagerTest {
    private LspProcessManagerTest() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected the sqldeps executable path.");
        }

        startsServerAndRequestsDiagnostics(Path.of(args[0]));

        System.out.println("sqldeps: LSP process manager tests ok");
    }

    private static void startsServerAndRequestsDiagnostics(Path executable) throws Exception {
        List<String> command = List.of(executable.toString(), "lsp");
        DocumentSnapshot snapshot = new DocumentSnapshot(
            "file:///tmp/sqldeps-java-lsp-test.sql",
            1,
            1,
            "SELECT 1;"
        );

        try (LspProcessManager manager = LspProcessManager.start(
            command,
            null,
            null,
            true,
            ignored -> {
            },
            ignored -> {
            })) {
            manager.didOpen(snapshot);
            DocumentDiagnosticReport report = manager
                .requestDiagnostics(snapshot.uri())
                .get(10, TimeUnit.SECONDS);

            check(report != null, "diagnostic response");
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
