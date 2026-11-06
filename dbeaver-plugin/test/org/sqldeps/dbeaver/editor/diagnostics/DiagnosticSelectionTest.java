package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.List;

import org.eclipse.lsp4j.Diagnostic;

public final class DiagnosticSelectionTest {
    private DiagnosticSelectionTest() {
    }

    public static void main(String[] args) {
        nextAndPreviousWrapAroundTheCurrentDiagnostics();
        replacingDiagnosticsResetsTheSelection();
        emptyDiagnosticsDoNotSelectAnything();

        System.out.println("sqldeps: DBeaver diagnostic selection tests ok");
    }

    private static void nextAndPreviousWrapAroundTheCurrentDiagnostics() {
        Diagnostic first = diagnostic("first");
        Diagnostic second = diagnostic("second");
        DiagnosticSelection selection = new DiagnosticSelection();
        selection.replace(List.of(first, second));

        check(selection.first() == first, "first diagnostic is selected");
        check(selection.next() == second, "next diagnostic is selected");
        check(selection.next() == first, "next navigation wraps");
        check(selection.previous() == second, "previous navigation wraps");
    }

    private static void replacingDiagnosticsResetsTheSelection() {
        Diagnostic first = diagnostic("first");
        Diagnostic replacement = diagnostic("replacement");
        DiagnosticSelection selection = new DiagnosticSelection();
        selection.replace(List.of(first));
        selection.next();
        selection.replace(List.of(replacement));

        check(selection.next() == replacement, "replacement resets the navigation index");
    }

    private static void emptyDiagnosticsDoNotSelectAnything() {
        DiagnosticSelection selection = new DiagnosticSelection();

        check(selection.first() == null, "empty first selection");
        check(selection.next() == null, "empty next selection");
        check(selection.previous() == null, "empty previous selection");
    }

    private static Diagnostic diagnostic(String message) {
        Diagnostic diagnostic = new Diagnostic();
        diagnostic.setMessage(message);
        return diagnostic;
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
