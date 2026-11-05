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
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.sqldeps.dbeaver.lsp.DocumentSnapshot;

/** Coalesces rapid editor changes before sending a full-text LSP update. */
final class DocumentChangeDebouncer implements AutoCloseable {
    private final ScheduledExecutorService scheduler;
    private final long delayMilliseconds;
    private final Supplier<DocumentSnapshot> snapshotSupplier;
    private final Consumer<DocumentSnapshot> changeConsumer;

    private ScheduledFuture<?> pendingChange;
    private boolean closed;

    DocumentChangeDebouncer(
        ScheduledExecutorService scheduler,
        long delayMilliseconds,
        Supplier<DocumentSnapshot> snapshotSupplier,
        Consumer<DocumentSnapshot> changeConsumer
    ) {
        this.scheduler = scheduler;
        this.delayMilliseconds = delayMilliseconds;
        this.snapshotSupplier = snapshotSupplier;
        this.changeConsumer = changeConsumer;
    }

    synchronized void schedule() {
        if (closed) {
            return;
        }

        if (pendingChange != null) {
            pendingChange.cancel(false);
        }

        pendingChange = scheduler.schedule(this::publishSnapshot, delayMilliseconds, TimeUnit.MILLISECONDS);
    }

    @Override
    public synchronized void close() {
        closed = true;

        if (pendingChange != null) {
            pendingChange.cancel(false);
            pendingChange = null;
        }
    }

    private void publishSnapshot() {
        synchronized (this) {
            if (closed) {
                return;
            }

            pendingChange = null;
        }

        changeConsumer.accept(snapshotSupplier.get());
    }
}
