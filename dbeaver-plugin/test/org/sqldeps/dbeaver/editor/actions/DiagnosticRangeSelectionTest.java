package org.sqldeps.dbeaver.editor.actions;

import java.util.List;

import org.eclipse.jface.text.Document;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Range;

public final class DiagnosticRangeSelectionTest {
    private DiagnosticRangeSelectionTest() {
    }

    public static void main(String[] args) throws Exception {
        exactRangeWinsWhenMultipleDiagnosticsOverlap();
        mostSpecificOverlapIsUsedWhenNoExactRangeExists();
        allOverlappingDiagnosticsAreReturned();
        nonOverlappingDiagnosticsAreIgnored();

        System.out.println("sqldeps: DBeaver diagnostic range selection tests ok");
    }

    private static void exactRangeWinsWhenMultipleDiagnosticsOverlap() throws Exception {
        Document document = new Document("SELECT 1;");
        Diagnostic broad = diagnostic(0, 0, 0, 8, "broad");
        Diagnostic exact = diagnostic(0, 7, 0, 8, "exact");

        List<Diagnostic> selected = DiagnosticRangeSelection.select(
            document,
            exact.getRange(),
            List.of(broad, exact)
        );

        check(selected.size() == 1, "exact selection returns one diagnostic");
        check(selected.get(0) == exact, "exact diagnostic wins over broad overlap");
    }

    private static void mostSpecificOverlapIsUsedWhenNoExactRangeExists() throws Exception {
        Document document = new Document("SELECT 1;");
        Diagnostic first = diagnostic(0, 0, 0, 6, "first");
        Diagnostic second = diagnostic(0, 5, 0, 8, "second");

        List<Diagnostic> selected = DiagnosticRangeSelection.select(
            document,
            diagnostic(0, 4, 0, 7, "cursor" ).getRange(),
            List.of(first, second)
        );

        check(selected.size() == 1, "ambiguous selection returns one diagnostic");
        check(selected.get(0) == second, "most specific overlapping diagnostic is selected");
    }

    private static void nonOverlappingDiagnosticsAreIgnored() throws Exception {
        Document document = new Document("SELECT 1;");
        Diagnostic diagnostic = diagnostic(0, 0, 0, 6, "select");

        List<Diagnostic> selected = DiagnosticRangeSelection.select(
            document,
            new Range(new org.eclipse.lsp4j.Position(0, 7), new org.eclipse.lsp4j.Position(0, 8)),
            List.of(diagnostic)
        );

        check(selected.isEmpty(), "non-overlapping diagnostic is ignored");
    }

    private static void allOverlappingDiagnosticsAreReturned() throws Exception {
        Document document = new Document("SELECT * FROM upInfo ui;");
        Diagnostic broad = diagnostic(0, 0, 0, 23, "unfiltered-select");
        Diagnostic specific = diagnostic(0, 7, 0, 8, "select-star");

        List<Diagnostic> selected = DiagnosticRangeSelection.selectAll(
            document,
            new Range(new org.eclipse.lsp4j.Position(0, 7), new org.eclipse.lsp4j.Position(0, 8)),
            List.of(broad, specific)
        );

        check(selected.size() == 2, "all overlapping diagnostics are returned");
        check(selected.get(0) == broad, "overlapping diagnostics preserve server order");
        check(selected.get(1) == specific, "specific overlapping diagnostic is preserved");
    }

    private static Diagnostic diagnostic(
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        String message
    ) {
        Diagnostic diagnostic = new Diagnostic();
        diagnostic.setRange(
            new Range(
                new org.eclipse.lsp4j.Position(startLine, startCharacter),
                new org.eclipse.lsp4j.Position(endLine, endCharacter)
            )
        );
        diagnostic.setMessage(message);
        return diagnostic;
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
