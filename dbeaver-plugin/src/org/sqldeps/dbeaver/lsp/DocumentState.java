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

/** Thread-safe version and generation state for one unsaved editor document. */
public final class DocumentState {
    private final String uri;
    private int version;
    private long generation;
    private String text;
    private String synchronizedText;

    /** Creates the initial version-one snapshot. */
    public DocumentState(String uri, String text) {
        this.uri = uri;
        this.text = text;
        synchronizedText = text;
        version = 1;
        generation = 1;
    }

    /** Returns an immutable snapshot that can be handed to asynchronous work. */
    public synchronized DocumentSnapshot snapshot() {
        return new DocumentSnapshot(uri, version, generation, text);
    }

    /** Replaces the text and advances both the LSP version and local generation. */
    public synchronized DocumentSnapshot update(String newText) {
        text = newText;
        version++;
        generation++;

        return snapshot();
    }

    /** Returns the next range-based change and advances the synchronization baseline. */
    public synchronized DocumentChange nextChange() {
        DocumentSnapshot currentSnapshot = snapshot();
        DocumentChange change = DocumentChange.between(synchronizedText, currentSnapshot);
        synchronizedText = text;

        return change;
    }

    /** Returns whether an asynchronous result still belongs to the current document. */
    public synchronized boolean isCurrent(DocumentSnapshot snapshot) {
        return generation == snapshot.generation() && version == snapshot.version();
    }

    /** Returns the stable document URI used by the LSP session. */
    public synchronized String uri() {
        return uri;
    }
}

