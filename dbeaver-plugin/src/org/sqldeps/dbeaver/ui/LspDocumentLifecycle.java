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

import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.LspConnection;

/** Owns document open/change/close calls and session shutdown ordering. */
final class LspDocumentLifecycle implements AutoCloseable {
    @FunctionalInterface
    interface ConnectionFactory {
        LspConnection open() throws Exception;
    }

    private final ScheduledExecutorService scheduler;
    private final String documentUri;
    private final ConnectionFactory connectionFactory;
    private final Consumer<LspConnection> documentOpened;
    private final Consumer<Exception> startFailure;

    private volatile LspConnection connection;
    private volatile boolean closed;

    LspDocumentLifecycle(
        ScheduledExecutorService scheduler,
        String documentUri,
        ConnectionFactory connectionFactory,
        Consumer<LspConnection> documentOpened,
        Consumer<Exception> startFailure
    ) {
        this.scheduler = scheduler;
        this.documentUri = documentUri;
        this.connectionFactory = connectionFactory;
        this.documentOpened = documentOpened;
        this.startFailure = startFailure;
    }

    void start() {
        startConnection();
    }

    void restart() {
        if (closed) {
            return;
        }

        scheduler.execute(() -> {
            closeConnection();

            if (!closed) {
                startConnection();
            }
        });
    }

    boolean isRunning() {
        return !closed && connection != null;
    }

    LspConnection connection() {
        return connection;
    }

    void didChange(DocumentChange change) {
        LspConnection currentConnection = connection;
        if (currentConnection != null && !closed) {
            currentConnection.didChange(change);
        }
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        LspConnection currentConnection = connection;
        connection = null;
        if (currentConnection != null) {
            scheduler.execute(() -> closeConnection(currentConnection));
        }
    }

    private void startConnection() {
        LspConnection newConnection = null;
        try {
            newConnection = connectionFactory.open();
            connection = newConnection;

            if (closed) {
                connection = null;
                closeConnection(newConnection);
                return;
            }

            documentOpened.accept(newConnection);
        }

        catch (Exception exception) {
            if (newConnection != null) {
                connection = null;
                closeConnection(newConnection);
            }

            startFailure.accept(exception);
        }
    }

    private void closeConnection() {
        LspConnection currentConnection = connection;
        connection = null;
        if (currentConnection != null) {
            closeConnection(currentConnection);
        }
    }

    private void closeConnection(LspConnection connectionToClose) {
        connectionToClose.didClose(documentUri);
        connectionToClose.close();
    }
}
