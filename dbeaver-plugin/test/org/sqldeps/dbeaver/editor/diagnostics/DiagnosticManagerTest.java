package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Set;

import org.eclipse.jface.text.Document;
import org.eclipse.jface.text.Position;
import org.eclipse.jface.text.source.Annotation;
import org.eclipse.jface.text.source.AnnotationModel;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DiagnosticSeverity;
import org.eclipse.lsp4j.Range;

public final class DiagnosticManagerTest {
    private DiagnosticManagerTest() {
    }

    public static void main(String[] args) {
        replaceUsesUtf16OffsetsAndAnnotationMetadata();
        replaceMapsDiagnosticSeverities();
        replaceSkipsDiagnosticsOutsideTheDocument();
        replaceClearsOwnedAnnotationsWhenDiagnosticsDisappear();

        System.out.println("sqldeps: DBeaver diagnostic manager tests ok");
    }

    private static void replaceUsesUtf16OffsetsAndAnnotationMetadata() {
        Document document = new Document("SELECT 😀;");
        AnnotationModel model = new AnnotationModel();
        Diagnostic diagnostic = diagnostic(
            new Range(new org.eclipse.lsp4j.Position(0, 7), new org.eclipse.lsp4j.Position(0, 9)),
            "Emoji token",
            DiagnosticSeverity.Warning);

        new DiagnosticManager().replace(model, document, List.of(diagnostic));

        Annotation annotation = onlyAnnotation(model);
        Position position = model.getPosition(annotation);
        check(position.getOffset() == 7, "UTF-16 diagnostic offset");
        check(position.getLength() == 2, "UTF-16 diagnostic length");
        check(annotation.getType().equals("org.eclipse.ui.workbench.texteditor.warning"), "warning annotation type");
        check(((SqldepsProblemAnnotation)annotation).getText().equals("Emoji token"), "diagnostic message");
    }

    private static void replaceClearsOwnedAnnotationsWhenDiagnosticsDisappear() {
        Document document = new Document("SELECT 1;");
        AnnotationModel model = new AnnotationModel();
        DiagnosticManager manager = new DiagnosticManager();

        manager.replace(
            model,
            document,
            List.of(diagnostic(
                new Range(new org.eclipse.lsp4j.Position(0, 0), new org.eclipse.lsp4j.Position(0, 6)),
                "Select statement",
                DiagnosticSeverity.Information)));
        manager.replace(model, document, List.of());

        check(!model.getAnnotationIterator().hasNext(), "owned annotations are cleared");
    }

    private static void replaceMapsDiagnosticSeverities() {
        Document document = new Document("SELECT 1;\nSELECT 2;");
        AnnotationModel model = new AnnotationModel();
        DiagnosticManager manager = new DiagnosticManager();

        manager.replace(
            model,
            document,
            List.of(
                diagnostic(
                    new Range(new org.eclipse.lsp4j.Position(0, 0), new org.eclipse.lsp4j.Position(0, 6)),
                    "error",
                    DiagnosticSeverity.Error),
                diagnostic(
                    new Range(new org.eclipse.lsp4j.Position(1, 0), new org.eclipse.lsp4j.Position(1, 6)),
                    "info",
                    DiagnosticSeverity.Information)));

        Iterator<Annotation> annotations = model.getAnnotationIterator();
        Set<String> annotationTypes = new HashSet<>();
        annotationTypes.add(annotations.next().getType());
        annotationTypes.add(annotations.next().getType());

        check(
            annotationTypes.contains("org.eclipse.ui.workbench.texteditor.error"),
            "error annotation type"
        );
        check(
            annotationTypes.contains("org.eclipse.ui.workbench.texteditor.info"),
            "information annotation type"
        );
    }

    private static void replaceSkipsDiagnosticsOutsideTheDocument() {
        Document document = new Document("SELECT 1;");
        AnnotationModel model = new AnnotationModel();
        DiagnosticManager manager = new DiagnosticManager();

        manager.replace(
            model,
            document,
            List.of(diagnostic(
                new Range(new org.eclipse.lsp4j.Position(4, 0), new org.eclipse.lsp4j.Position(4, 1)),
                "outside",
                DiagnosticSeverity.Error)));

        check(!model.getAnnotationIterator().hasNext(), "invalid diagnostic range is ignored");
    }

    private static Diagnostic diagnostic(Range range, String message, DiagnosticSeverity severity) {
        Diagnostic diagnostic = new Diagnostic();
        diagnostic.setRange(range);
        diagnostic.setMessage(message);
        diagnostic.setSeverity(severity);
        return diagnostic;
    }

    private static Annotation onlyAnnotation(AnnotationModel model) {
        Iterator<Annotation> annotations = model.getAnnotationIterator();
        check(annotations.hasNext(), "diagnostic annotation exists");
        Annotation annotation = annotations.next();
        check(!annotations.hasNext(), "exactly one diagnostic annotation exists");
        return annotation;
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
