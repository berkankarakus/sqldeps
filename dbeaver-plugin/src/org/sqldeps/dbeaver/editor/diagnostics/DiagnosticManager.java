/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.Position;
import org.eclipse.jface.text.source.Annotation;
import org.eclipse.jface.text.source.IAnnotationModel;
import org.eclipse.jface.text.source.IAnnotationModelExtension;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.DiagnosticTag;
import org.eclipse.lsp4j.DiagnosticSeverity;
import org.eclipse.lsp4j.Range;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;

/** Maps LSP diagnostics to DBeaver SQL editor annotations and hover text. */
public final class DiagnosticManager {
    private static final String ERROR_ANNOTATION = "org.eclipse.ui.workbench.texteditor.error";
    private static final String WARNING_ANNOTATION = "org.eclipse.ui.workbench.texteditor.warning";
    private static final String INFO_ANNOTATION = "org.eclipse.ui.workbench.texteditor.info";
    private static final String UNNECESSARY_ANNOTATION = "org.sqldeps.dbeaver.unnecessary";

    private final Map<Annotation, Position> annotations = new LinkedHashMap<>();

    /** Replaces only annotations previously owned by this manager. */
    public void replace(IAnnotationModel model, IDocument document, List<Diagnostic> diagnostics) {
        if (!(model instanceof IAnnotationModelExtension extension)) {
            return;
        }

        Map<Annotation, Position> replacements = new LinkedHashMap<>();
        diagnostics.stream()
            .sorted(Comparator
                .comparingInt((Diagnostic diagnostic) -> diagnostic.getRange().getStart().getLine())
                .thenComparingInt(diagnostic -> diagnostic.getRange().getStart().getCharacter())
                .thenComparing(Diagnostic::getMessage))
            .forEach(diagnostic -> addAnnotation(document, replacements, diagnostic));

        extension.replaceAnnotations(
            annotations.keySet().toArray(Annotation[]::new),
            replacements
        );
        annotations.clear();
        annotations.putAll(replacements);
    }

    private static void addAnnotation(
        IDocument document,
        Map<Annotation, Position> replacements,
        Diagnostic diagnostic
    ) {
        if (!isValidRange(document, diagnostic.getRange())) {
            return;
        }

        try {
            int start = Utf16DocumentPosition.toOffset(document, diagnostic.getRange().getStart());
            int end = Utf16DocumentPosition.toOffset(document, diagnostic.getRange().getEnd());
            int length = Math.max(1, end - start);
            Annotation annotation = new SqldepsProblemAnnotation(
                annotationType(diagnostic),
                diagnostic.getMessage()
            );
            replacements.put(annotation, new Position(start, length));
        }

        catch (BadLocationException exception) {
            // A diagnostic whose range is no longer valid must not break the editor.
        }
    }

    private static boolean isValidRange(IDocument document, Range range) {
        if (range == null || !isValidPosition(document, range.getStart()) || !isValidPosition(document, range.getEnd())) {
            return false;
        }

        org.eclipse.lsp4j.Position start = range.getStart();
        org.eclipse.lsp4j.Position end = range.getEnd();

        return start.getLine() < end.getLine()
            || start.getLine() == end.getLine() && start.getCharacter() <= end.getCharacter();
    }

    private static boolean isValidPosition(IDocument document, org.eclipse.lsp4j.Position position) {
        if (position == null || position.getLine() < 0 || position.getLine() >= document.getNumberOfLines()) {
            return false;
        }

        try {
            return position.getCharacter() >= 0
                && position.getCharacter() <= document.getLineLength(position.getLine());
        }

        catch (BadLocationException exception) {
            return false;
        }
    }

    private static String annotationType(Diagnostic diagnostic) {
        if (diagnostic.getTags() != null && diagnostic.getTags().contains(DiagnosticTag.Unnecessary)) {
            return UNNECESSARY_ANNOTATION;
        }

        DiagnosticSeverity severity = diagnostic.getSeverity();
        if (severity == DiagnosticSeverity.Error) {
            return ERROR_ANNOTATION;
        }

        if (severity == DiagnosticSeverity.Warning) {
            return WARNING_ANNOTATION;
        }

        return INFO_ANNOTATION;
    }

}

