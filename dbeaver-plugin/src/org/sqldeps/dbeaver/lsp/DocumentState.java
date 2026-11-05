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

public final class DocumentState {
    private final String uri;
    private int version;
    private long generation;
    private String text;

    public DocumentState(String uri, String text) {
        this.uri = uri;
        this.text = text;
        version = 1;
        generation = 1;
    }

    public synchronized DocumentSnapshot snapshot() {
        return new DocumentSnapshot(uri, version, generation, text);
    }

    public synchronized DocumentSnapshot update(String newText) {
        text = newText;
        version++;
        generation++;

        return snapshot();
    }

    public synchronized boolean isCurrent(DocumentSnapshot snapshot) {
        return generation == snapshot.generation() && version == snapshot.version();
    }

    public synchronized String uri() {
        return uri;
    }
}

