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
package org.sqldeps.dbeaver.editor.actions;

import java.util.List;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;

/** Selects diagnostics that overlap a DBeaver quick-fix request. */
final class DiagnosticRangeSelection {
    private DiagnosticRangeSelection() {
    }

    static List<Diagnostic> select(
        IDocument document,
        Range range,
        List<Diagnostic> diagnostics
    ) throws BadLocationException {
        List<Diagnostic> matches = selectAll(document, range, diagnostics);

        if (matches.size() <= 1) {
            return matches;
        }

        for (Diagnostic diagnostic : matches) {
            if (sameRange(range, diagnostic.getRange())) {
                return List.of(diagnostic);
            }
        }

        Diagnostic selected = matches.get(0);
        int selectedLength = rangeLength(document, selected.getRange());
        for (Diagnostic diagnostic : matches.subList(1, matches.size())) {
            int candidateLength = rangeLength(document, diagnostic.getRange());
            if (candidateLength < selectedLength) {
                selected = diagnostic;
                selectedLength = candidateLength;
            }
        }

        return List.of(selected);
    }

    static List<Diagnostic> selectAll(
        IDocument document,
        Range range,
        List<Diagnostic> diagnostics
    ) throws BadLocationException {
        int rangeStart = toOffset(document, range.getStart());
        int rangeEnd = Math.max(rangeStart + 1, toOffset(document, range.getEnd()));
        return diagnostics.stream()
            .filter(diagnostic -> overlaps(document, rangeStart, rangeEnd, diagnostic.getRange()))
            .toList();
    }

    static boolean overlaps(IDocument document, Range first, Range second) {
        try {
            int firstStart = toOffset(document, first.getStart());
            int firstEnd = Math.max(firstStart + 1, toOffset(document, first.getEnd()));
            return overlaps(document, firstStart, firstEnd, second);
        }

        catch (BadLocationException exception) {
            return false;
        }
    }

    private static boolean overlaps(
        IDocument document,
        int rangeStart,
        int rangeEnd,
        Range diagnosticRange
    ) {
        try {
            int diagnosticStart = toOffset(document, diagnosticRange.getStart());
            int diagnosticEnd = Math.max(diagnosticStart + 1, toOffset(document, diagnosticRange.getEnd()));
            return diagnosticStart < rangeEnd && rangeStart < diagnosticEnd;
        }

        catch (BadLocationException exception) {
            return false;
        }
    }

    private static int toOffset(IDocument document, Position position) throws BadLocationException {
        return Utf16DocumentPosition.toOffset(document, position);
    }

    private static int rangeLength(IDocument document, Range range) throws BadLocationException {
        int start = toOffset(document, range.getStart());
        int end = toOffset(document, range.getEnd());

        return Math.max(1, end - start);
    }

    private static boolean sameRange(Range first, Range second) {
        return first.getStart().getLine() == second.getStart().getLine()
            && first.getStart().getCharacter() == second.getStart().getCharacter()
            && first.getEnd().getLine() == second.getEnd().getLine()
            && first.getEnd().getCharacter() == second.getEnd().getCharacter();
    }
}
