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
        sharesOneProcessBetweenDocuments(Path.of(args[0]));

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

    private static void sharesOneProcessBetweenDocuments(Path executable) throws Exception {
        List<String> command = List.of(executable.toString(), "lsp");
        DocumentSnapshot firstSnapshot = new DocumentSnapshot(
            "file:///tmp/sqldeps-java-lsp-first.sql",
            1,
            1,
            "SELECT 1;"
        );
        DocumentSnapshot secondSnapshot = new DocumentSnapshot(
            "file:///tmp/sqldeps-java-lsp-second.sql",
            1,
            1,
            "SELECT 2;"
        );

        LspConnection first = LspProcessPool.acquire(
            command,
            null,
            null,
            true,
            ignored -> {
            }
        );
        LspConnection second = LspProcessPool.acquire(
            command,
            null,
            null,
            true,
            ignored -> {
            }
        );

        try {
            check(LspProcessPool.activeProcessCount() == 1, "one shared process");
            first.didOpen(firstSnapshot);
            second.didOpen(secondSnapshot);
            check(
                first.requestDiagnostics(firstSnapshot.uri()).get(10, TimeUnit.SECONDS) != null,
                "first shared document"
            );
            check(
                second.requestDiagnostics(secondSnapshot.uri()).get(10, TimeUnit.SECONDS) != null,
                "second shared document"
            );

            first.didClose(firstSnapshot.uri());
            first.close();
            check(
                second.requestDiagnostics(secondSnapshot.uri()).get(10, TimeUnit.SECONDS) != null,
                "remaining shared document"
            );
        }

        finally {
            second.didClose(secondSnapshot.uri());
            second.close();
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
