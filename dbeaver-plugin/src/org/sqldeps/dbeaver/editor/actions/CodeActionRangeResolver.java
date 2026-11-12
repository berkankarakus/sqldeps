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
import java.util.function.Supplier;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;

/** Converts editor selections and offsets into LSP ranges for code-action requests. */
final class CodeActionRangeResolver {
    private final IDocument document;
    private final Supplier<List<Diagnostic>> diagnosticsSupplier;

    CodeActionRangeResolver(
        IDocument document,
        Supplier<List<Diagnostic>> diagnosticsSupplier
    ) {
        this.document = document;
        this.diagnosticsSupplier = diagnosticsSupplier;
    }

    Range selectedRange(ISelection selection) throws BadLocationException {
        int offset = selectionOffset(selection);
        int length = selectionLength(selection);

        int start = clampOffset(offset);
        int end = Math.max(start, clampOffset((long)start + Math.max(0, length)));

        return rangeAt(start, end - start);
    }

    Range rangeAt(int offset, int length) throws BadLocationException {
        int start = clampOffset(offset);
        long requestedEnd = (long)start + Math.max(0, length);
        int end = (int)Math.min(document.getLength(), requestedEnd);

        return new Range(positionAt(start), positionAt(end));
    }

    List<Diagnostic> diagnosticsFor(Range range) throws BadLocationException {
        return DiagnosticRangeSelection.select(document, range, diagnosticsSupplier.get());
    }

    List<Diagnostic> allDiagnosticsFor(Range range) throws BadLocationException {
        return DiagnosticRangeSelection.selectAll(document, range, diagnosticsSupplier.get());
    }

    private int clampOffset(long offset) {
        return (int)Math.max(0, Math.min(offset, document.getLength()));
    }

    private static int selectionOffset(ISelection selection) {
        if (selection instanceof ITextSelection textSelection) {
            return textSelection.getOffset();
        }

        return 0;
    }

    private static int selectionLength(ISelection selection) {
        if (selection instanceof ITextSelection textSelection) {
            return textSelection.getLength();
        }

        return 0;
    }

    private Position positionAt(int offset) throws BadLocationException {
        int line = document.getLineOfOffset(offset);

        return new Position(line, offset - document.getLineOffset(line));
    }
}
