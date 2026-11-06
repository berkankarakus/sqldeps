package org.sqldeps.dbeaver.editor;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.DocumentPosition;
import org.sqldeps.dbeaver.lsp.DocumentRange;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.DocumentState;

public final class DocumentChangeDebouncerTest {
    private DocumentChangeDebouncerTest() {
    }

    public static void main(String[] args) throws Exception {
        coalescesRapidChangesAndPublishesLatestSnapshot();
        closeCancelsPendingChanges();

        System.out.println("sqldeps: Java document debounce tests ok");
    }

    private static void coalescesRapidChangesAndPublishesLatestSnapshot() throws Exception {
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        DocumentState state = new DocumentState("file:///tmp/query.sql", "SELECT 1;");
        CountDownLatch callback = new CountDownLatch(1);
        AtomicInteger callbackCount = new AtomicInteger();
        AtomicReference<DocumentChange> receivedChange = new AtomicReference<>();

        try {
            try (DocumentChangeDebouncer debouncer = new DocumentChangeDebouncer(
                scheduler,
                25,
                state::nextChange,
                change -> {
                    callbackCount.incrementAndGet();
                    receivedChange.set(change);
                    callback.countDown();
                })) {
                state.update("SELECT 2;");
                debouncer.schedule();
                state.update("SELECT 3;");
                debouncer.schedule();

                check(callback.await(1, TimeUnit.SECONDS), "debounced callback completion");
                check(callbackCount.get() == 1, "rapid changes coalesced");
                check(
                    "SELECT 3;".equals(receivedChange.get().snapshot().text()),
                    "latest snapshot published"
                );
            }
        }

        finally {
            scheduler.shutdownNow();
        }
    }

    private static void closeCancelsPendingChanges() throws Exception {
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        CountDownLatch callback = new CountDownLatch(1);

        try {
            try (DocumentChangeDebouncer debouncer = new DocumentChangeDebouncer(
                scheduler,
                100,
                () -> new DocumentChange(
                    new DocumentSnapshot(
                        "file:///tmp/query.sql",
                        1,
                        1,
                        "SELECT 1;"
                    ),
                    new DocumentRange(new DocumentPosition(0, 0), new DocumentPosition(0, 0)),
                    0,
                    ""
                ),
                ignored -> callback.countDown())) {
                debouncer.schedule();
            }

            check(!callback.await(250, TimeUnit.MILLISECONDS), "close cancels pending callback");
        }

        finally {
            scheduler.shutdownNow();
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
