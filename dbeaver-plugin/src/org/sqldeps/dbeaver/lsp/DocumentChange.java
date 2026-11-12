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
package org.sqldeps.dbeaver.lsp;

/** A debounced, range-based LSP change relative to the last synchronized text. */
public record DocumentChange(
    DocumentSnapshot snapshot,
    DocumentRange range,
    int rangeLength,
    String text
) {
    /** Builds the smallest UTF-16 range that transforms the previous text. */
    public static DocumentChange between(String previousText, DocumentSnapshot snapshot) {
        String currentText = snapshot.text();
        int prefixLength = commonPrefixLength(previousText, currentText);
        int previousEnd = previousText.length();
        int currentEnd = currentText.length();

        while (previousEnd > prefixLength
            && currentEnd > prefixLength
            && previousText.charAt(previousEnd - 1) == currentText.charAt(currentEnd - 1)) {
            previousEnd--;
            currentEnd--;
        }

        DocumentRange range = new DocumentRange(
            positionAt(previousText, prefixLength),
            positionAt(previousText, previousEnd)
        );

        return new DocumentChange(
            snapshot,
            range,
            previousEnd - prefixLength,
            currentText.substring(prefixLength, currentEnd)
        );
    }

    private static int commonPrefixLength(String previousText, String currentText) {
        int limit = Math.min(previousText.length(), currentText.length());
        int index = 0;

        while (index < limit && previousText.charAt(index) == currentText.charAt(index)) {
            index++;
        }

        return index;
    }

    private static DocumentPosition positionAt(String text, int offset) {
        int line = 0;
        int lineStart = 0;

        for (int index = 0; index < offset; index++) {
            if (text.charAt(index) == '\n') {
                line++;
                lineStart = index + 1;
            }
        }

        int lineEnd = text.indexOf('\n', lineStart);
        if (lineEnd < 0) {
            lineEnd = text.length();
        }
        if (lineEnd > lineStart && text.charAt(lineEnd - 1) == '\r') {
            lineEnd--;
        }

        return new DocumentPosition(line, Math.min(offset - lineStart, lineEnd - lineStart));
    }
}
