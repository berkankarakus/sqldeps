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
package org.sqldeps.dbeaver.ui;

import java.util.concurrent.ScheduledExecutorService;
import java.util.function.Consumer;

import org.eclipse.jface.text.DocumentEvent;
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.IDocumentListener;
import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.DocumentState;

/** Bridges DBeaver document events to debounced, versioned LSP snapshots. */
public final class DocumentSynchronizer implements AutoCloseable {
    private static final long DEBOUNCE_MILLISECONDS = 300;

    private final IDocument document;
    private final DocumentState state;
    private final DocumentChangeDebouncer changeDebouncer;
    private final IDocumentListener documentListener = new DocumentListener();

    /** Starts synchronization for one editor document. */
    public DocumentSynchronizer(
        IDocument document,
        String uri,
        ScheduledExecutorService scheduler,
        Consumer<DocumentChange> changeConsumer
    ) {
        this.document = document;
        state = new DocumentState(uri, document.get());
        changeDebouncer = new DocumentChangeDebouncer(
            scheduler,
            DEBOUNCE_MILLISECONDS,
            state::nextChange,
            changeConsumer
        );
        document.addDocumentListener(documentListener);
    }

    /** Returns the latest immutable document snapshot. */
    public DocumentSnapshot snapshot() {
        return state.snapshot();
    }

    /** Returns whether a diagnostic result can still be applied to the editor. */
    public boolean isCurrent(DocumentSnapshot snapshot) {
        return state.isCurrent(snapshot);
    }

    /** Returns the stable URI associated with this editor document. */
    public String uri() {
        return state.uri();
    }

    @Override
    public void close() {
        document.removeDocumentListener(documentListener);
        changeDebouncer.close();
    }

    private final class DocumentListener implements IDocumentListener {
        @Override
        public void documentAboutToBeChanged(DocumentEvent event) {
        }

        @Override
        public void documentChanged(DocumentEvent event) {
            state.update(event.getDocument().get());
            changeDebouncer.schedule();
        }
    }
}

