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

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.Document;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.Utf16DocumentPosition;

/** Validates and resolves bounded, single-document workspace edits. */
final class WorkspaceEditSafety {
    private static final int MAX_EDIT_COUNT = 1_000;
    private static final int MAX_TEXT_CHARACTERS = 1_000_000;

    private WorkspaceEditSafety() {
    }

    static boolean isApplicable(
        DocumentSnapshot snapshot,
        boolean snapshotIsCurrent,
        WorkspaceEdit edit
    ) {
        if (!snapshotIsCurrent || !hasSingleTextChange(edit)) {
            return false;
        }

        return edit.getChanges().containsKey(snapshot.uri());
    }

    static ExternalEdit resolveExternalConfigurationEdit(
        DocumentSnapshot snapshot,
        boolean snapshotIsCurrent,
        WorkspaceEdit edit
    ) {
        if (!hasSingleExternalTextChange(snapshotIsCurrent, edit)) {
            return null;
        }

        Map.Entry<String, List<TextEdit>> change = edit.getChanges().entrySet().iterator().next();
        if (snapshot.uri().equals(change.getKey())) {
            return null;
        }

        Path path = configurationPath(change.getKey());
        if (path == null || Files.isSymbolicLink(path)) {
            return null;
        }

        boolean createIfMissing = !Files.exists(path);
        if (createIfMissing && (path.getParent() == null || !Files.isDirectory(path.getParent()))) {
            return null;
        }

        if (!createIfMissing && !Files.isRegularFile(path)) {
            return null;
        }

        try {
            String originalText = createIfMissing
                ? ""
                : Files.readString(path, StandardCharsets.UTF_8);
            List<ResolvedTextEdit> edits = resolve(new Document(originalText), change.getValue());
            if (edits == null) {
                return null;
            }

            return new ExternalEdit(path, originalText, edits, createIfMissing);
        }

        catch (java.io.IOException exception) {
            return null;
        }
    }

    static List<ResolvedTextEdit> resolve(IDocument document, List<TextEdit> textEdits) {
        if (textEdits == null || textEdits.size() > MAX_EDIT_COUNT) {
            return null;
        }

        List<ResolvedTextEdit> resolvedEdits = new ArrayList<>();
        long totalNewTextCharacters = 0;
        for (TextEdit textEdit : textEdits) {
            if (!isSafeTextEdit(document, textEdit, totalNewTextCharacters)) {
                return null;
            }

            try {
                int start = Utf16DocumentPosition.toOffset(document, textEdit.getRange().getStart());
                int end = Utf16DocumentPosition.toOffset(document, textEdit.getRange().getEnd());
                if (end < start) {
                    return null;
                }

                totalNewTextCharacters += textEdit.getNewText().length();
                resolvedEdits.add(new ResolvedTextEdit(start, end, textEdit.getNewText()));
            }

            catch (BadLocationException exception) {
                return null;
            }
        }

        resolvedEdits.sort(Comparator.comparingInt(ResolvedTextEdit::start).reversed());
        if (hasOverlappingEdits(resolvedEdits)) {
            return null;
        }

        return resolvedEdits;
    }

    private static boolean isSafeTextEdit(
        IDocument document,
        TextEdit textEdit,
        long totalNewTextCharacters
    ) {
        if (textEdit == null || !isValidRange(document, textEdit.getRange())) {
            return false;
        }

        String newText = textEdit.getNewText();
        if (newText == null) {
            return false;
        }

        int textLength = newText.length();
        return textLength <= MAX_TEXT_CHARACTERS
            && totalNewTextCharacters + textLength <= MAX_TEXT_CHARACTERS;
    }

    private static boolean isValidRange(IDocument document, Range range) {
        if (range == null || range.getStart() == null || range.getEnd() == null) {
            return false;
        }

        return isValidPosition(document, range.getStart())
            && isValidPosition(document, range.getEnd());
    }

    private static boolean isValidPosition(IDocument document, Position position) {
        if (position.getLine() < 0 || position.getLine() >= document.getNumberOfLines()) {
            return false;
        }

        if (position.getCharacter() < 0) {
            return false;
        }

        try {
            return position.getCharacter() <= document.getLineLength(position.getLine());
        }

        catch (BadLocationException exception) {
            return false;
        }
    }

    private static boolean hasOverlappingEdits(List<ResolvedTextEdit> edits) {
        for (int index = 1; index < edits.size(); index++) {
            ResolvedTextEdit later = edits.get(index - 1);
            ResolvedTextEdit earlier = edits.get(index);

            if (later.start() < earlier.end()) {
                return true;
            }
        }

        return false;
    }

    private static Path configurationPath(String uriText) {
        try {
            URI uri = URI.create(uriText);
            if (!"file".equalsIgnoreCase(uri.getScheme()) || uri.getPath() == null) {
                return null;
            }

            Path path = Path.of(uri).toAbsolutePath().normalize();
            return ".sqldeps".equals(path.getFileName().toString()) ? path : null;
        }

        catch (IllegalArgumentException exception) {
            return null;
        }
    }

    private static boolean hasSingleExternalTextChange(
        boolean snapshotIsCurrent,
        WorkspaceEdit edit
    ) {
        return snapshotIsCurrent && hasSingleTextChange(edit);
    }

    private static boolean hasSingleTextChange(WorkspaceEdit edit) {
        if (edit == null || edit.getChanges() == null) {
            return false;
        }

        if (edit.getChanges().size() != 1) {
            return false;
        }

        return edit.getDocumentChanges() == null || edit.getDocumentChanges().isEmpty();
    }

    record ResolvedTextEdit(int start, int end, String newText) {
    }

    record ExternalEdit(
        Path path,
        String originalText,
        List<ResolvedTextEdit> edits,
        boolean createIfMissing
    ) {
    }
}
