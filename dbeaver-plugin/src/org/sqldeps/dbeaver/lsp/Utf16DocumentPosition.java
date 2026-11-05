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

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.Position;

public final class Utf16DocumentPosition {
    private Utf16DocumentPosition() {
    }

    public static int toOffset(IDocument document, Position position) throws BadLocationException {
        int line = Math.max(0, Math.min(position.getLine(), document.getNumberOfLines() - 1));
        int lineOffset = document.getLineOffset(line);
        int lineLength = document.getLineLength(line);
        int character = Math.max(0, Math.min(position.getCharacter(), lineLength));

        return lineOffset + character;
    }
}

