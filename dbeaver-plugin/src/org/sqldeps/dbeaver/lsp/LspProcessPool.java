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

import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.jsonrpc.messages.Either;

public final class LspProcessPool {
    private static final Map<PoolKey, SharedProcess> PROCESSES = new HashMap<>();

    private LspProcessPool() {
    }

    static int activeProcessCount() {
        synchronized (PROCESSES) {
            return PROCESSES.size();
        }
    }

    public static LspConnection acquire(
        List<String> command,
        String rootUri,
        String indexPath,
        boolean automaticIndexSelection,
        Consumer<String> logConsumer
    ) throws Exception {
        PoolKey key = new PoolKey(command, rootUri, indexPath, automaticIndexSelection);
        synchronized (PROCESSES) {
            SharedProcess sharedProcess = PROCESSES.get(key);
            if (sharedProcess == null) {
                LspProcessManager manager = LspProcessManager.start(
                    command,
                    rootUri,
                    indexPath,
                    automaticIndexSelection,
                    ignored -> {
                    },
                    logConsumer
                );
                sharedProcess = new SharedProcess(key, manager);
                PROCESSES.put(key, sharedProcess);
            }

            sharedProcess.referenceCount++;
            return new Lease(sharedProcess);
        }
    }

    private static void release(SharedProcess sharedProcess) {
        LspProcessManager managerToClose = null;
        synchronized (PROCESSES) {
            if (sharedProcess.referenceCount == 0) {
                return;
            }

            sharedProcess.referenceCount--;
            if (sharedProcess.referenceCount == 0
                && PROCESSES.remove(sharedProcess.key, sharedProcess)) {
                managerToClose = sharedProcess.manager;
            }
        }

        if (managerToClose != null) {
            managerToClose.close();
        }
    }

    private record PoolKey(
        List<String> command,
        String rootUri,
        String indexPath,
        boolean automaticIndexSelection
    ) {
        private PoolKey {
            command = List.copyOf(command);
        }
    }

    private static final class SharedProcess {
        private final PoolKey key;
        private final LspProcessManager manager;
        private int referenceCount;

        private SharedProcess(PoolKey key, LspProcessManager manager) {
            this.key = key;
            this.manager = manager;
        }
    }

    private static final class Lease implements LspConnection {
        private final SharedProcess sharedProcess;
        private boolean closed;

        private Lease(SharedProcess sharedProcess) {
            this.sharedProcess = sharedProcess;
        }

        @Override
        public void didOpen(DocumentSnapshot snapshot) {
            sharedProcess.manager.didOpen(snapshot);
        }

        @Override
        public void didChange(DocumentSnapshot snapshot) {
            sharedProcess.manager.didChange(snapshot);
        }

        @Override
        public void didClose(String uri) {
            sharedProcess.manager.didClose(uri);
        }

        @Override
        public CompletableFuture<DocumentDiagnosticReport> requestDiagnostics(String uri) {
            return sharedProcess.manager.requestDiagnostics(uri);
        }

        @Override
        public CompletableFuture<List<Either<Command, CodeAction>>> requestCodeActions(CodeActionParams params) {
            return sharedProcess.manager.requestCodeActions(params);
        }

        @Override
        public CompletableFuture<Object> executeCommand(Command command) {
            return sharedProcess.manager.executeCommand(command);
        }

        @Override
        public void close() {
            if (closed) {
                return;
            }

            closed = true;
            release(sharedProcess);
        }
    }
}
