package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.List;
import java.util.Map;

import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.jsonrpc.messages.Either;

public final class DiagnosticResultStatusTest {
    private DiagnosticResultStatusTest() {
    }

    public static void main(String[] args) {
        describesEmptyAndCountedResults();
        staleStatusTakesPriority();
        partialAnalysisIsReported();

        System.out.println("sqldeps: DBeaver diagnostic result status tests ok");
    }

    private static void describesEmptyAndCountedResults() {
        check(
            DiagnosticResultStatus.describe(List.of()).equals("up to date — no findings"),
            "empty result is current");
        check(
            DiagnosticResultStatus.describe(List.of(diagnostic(null, null, null)))
                .equals("up to date — 1 finding(s)"),
            "ordinary findings include their count");
    }

    private static void staleStatusTakesPriority() {
        Diagnostic stale = diagnostic("stale-index", null, Map.of("stale", true));

        check(DiagnosticResultStatus.hasStaleIndex(List.of(stale)), "stale index is detected");
        check(
            DiagnosticResultStatus.describe(List.of(stale)).startsWith("stale index"),
            "stale status takes priority");
    }

    private static void partialAnalysisIsReported() {
        Diagnostic partial = diagnostic(null, Either.forLeft("partial-analysis"), null);

        check(
            DiagnosticResultStatus.describe(List.of(partial)).startsWith("partial analysis"),
            "partial analysis is reported");
    }

    private static Diagnostic diagnostic(
        String source,
        Either<String, Integer> code,
        Object data
    ) {
        Diagnostic diagnostic = new Diagnostic();
        diagnostic.setSource(source);
        diagnostic.setCode(code);
        diagnostic.setData(data);
        return diagnostic;
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
