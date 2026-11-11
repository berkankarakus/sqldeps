package org.sqldeps.dbeaver.lsp;

import java.nio.file.Path;
import java.time.Duration;
import java.util.List;
import java.util.concurrent.TimeUnit;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionContext;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.jsonrpc.messages.Either;

public final class LspProcessManagerTest {
    private LspProcessManagerTest() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected the sqldeps executable path.");
        }

        startsServerAndRequestsDiagnostics(Path.of(args[0]));
        requestsNoLockQuickFix(Path.of(args[0]));
        requestsProcedureFormatting(Path.of(args[0]));
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

    private static void requestsNoLockQuickFix(Path executable) throws Exception {
        List<String> command = List.of(executable.toString(), "lsp");
        String uri = "file:///tmp/sqldeps-java-nolock-test.sql";
        DocumentSnapshot snapshot = new DocumentSnapshot(
            uri,
            1,
            1,
            "SELECT * FROM dbo.Info WITH (NOLOCK);"
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

            Diagnostic diagnostic = new Diagnostic();
            diagnostic.setSource("sqldeps");
            diagnostic.setCode(Either.forLeft("nolock"));
            diagnostic.setRange(new Range(
                new Position(0, 14),
                new Position(0, 24)
            ));

            CodeActionParams params = new CodeActionParams();
            params.setTextDocument(new TextDocumentIdentifier(uri));
            params.setRange(diagnostic.getRange());
            params.setContext(new CodeActionContext(List.of(diagnostic)));

            List<Either<org.eclipse.lsp4j.Command, CodeAction>> actions = manager
                .requestCodeActions(params)
                .get(10, TimeUnit.SECONDS);
            boolean hasNoLockRemoval = actions.stream()
                .filter(Either::isRight)
                .map(Either::getRight)
                .anyMatch(action -> "Remove NOLOCK table hint".equals(action.getTitle()));

            check(hasNoLockRemoval, "Java LSP client receives NOLOCK removal quick fix");
        }
    }

    private static void requestsProcedureFormatting(Path executable) throws Exception {
        List<String> command = List.of(executable.toString(), "lsp");
        String uri = "file:///tmp/sqldeps-java-format-test.sql";
        DocumentSnapshot snapshot = new DocumentSnapshot(
            uri,
            1,
            1,
            "CREATE PROCEDURE dbo.FormatProcedure @first INT, @second INT, @third INT AS SELECT 1;"
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
            var diagnostics = manager.requestDiagnostics(uri);
            List<? extends TextEdit> edits = manager
                .requestFormatting(uri)
                .get(10, TimeUnit.SECONDS);
            check(diagnostics.get(10, TimeUnit.SECONDS) != null, "diagnostics and formatting can run together");

            check(edits.size() == 1, "procedure formatting returns one full-document edit");
            check(
                edits.get(0).getNewText().contains("\n    @first INT,\n    @second INT,\n    @third INT\nAS"),
                "procedure formatting indents multiple parameters"
            );
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
