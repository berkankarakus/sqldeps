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

import java.util.concurrent.CompletableFuture;

final class DiagnosticRequestGate implements AutoCloseable {
    private long currentSequence;
    private CompletableFuture<?> pendingRequest;

    synchronized long begin() {
        currentSequence++;
        cancelPendingRequest();

        return currentSequence;
    }

    synchronized void track(long sequence, CompletableFuture<?> request) {
        if (sequence != currentSequence) {
            request.cancel(true);
            return;
        }

        pendingRequest = request;
    }

    synchronized boolean isCurrent(long sequence) {
        return sequence == currentSequence;
    }

    @Override
    public synchronized void close() {
        currentSequence++;
        cancelPendingRequest();
    }

    private void cancelPendingRequest() {
        CompletableFuture<?> request = pendingRequest;
        pendingRequest = null;

        if (request != null && !request.isDone()) {
            request.cancel(true);
        }
    }
}
