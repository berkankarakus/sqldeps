package org.sqldeps.dbeaver.editor;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import org.eclipse.jface.text.Document;
import org.sqldeps.dbeaver.lsp.DocumentChange;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;

public final class DocumentSynchronizerTest {
    private DocumentSynchronizerTest() {
    }

    public static void main(String[] args) throws Exception {
        changedDocumentPublishesLatestSnapshot();
        closePreventsPendingChangeCallback();

        System.out.println("sqldeps: DBeaver document synchronizer tests ok");
    }

    private static void changedDocumentPublishesLatestSnapshot() throws Exception {
        Document document = new Document("SELECT 1;");
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        CountDownLatch callback = new CountDownLatch(1);
        AtomicReference<DocumentChange> receivedChange = new AtomicReference<>();

        try (DocumentSynchronizer synchronizer = new DocumentSynchronizer(
            document,
            "file:///tmp/query.sql",
            scheduler,
            snapshot -> {
                receivedChange.set(snapshot);
                callback.countDown();
            })) {
            document.set("SELECT 2;");
            document.set("SELECT 3;");

            check(callback.await(2, TimeUnit.SECONDS), "document change callback completion");
            check(receivedChange.get().snapshot().version() == 3, "latest document version");
            check("SELECT 3;".equals(receivedChange.get().snapshot().text()), "latest document text");
            check(receivedChange.get().range().start().character() == 7, "changed range start");
            check(receivedChange.get().range().end().character() == 8, "changed range end");
            check(receivedChange.get().rangeLength() == 1, "changed range length");
            check("3".equals(receivedChange.get().text()), "changed range text");
        }

        finally {
            scheduler.shutdownNow();
        }
    }

    private static void closePreventsPendingChangeCallback() throws Exception {
        Document document = new Document("SELECT 1;");
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        CountDownLatch callback = new CountDownLatch(1);
        DocumentSynchronizer synchronizer = new DocumentSynchronizer(
            document,
            "file:///tmp/query.sql",
            scheduler,
            ignored -> callback.countDown());

        try {
            document.set("SELECT 2;");
            synchronizer.close();

            check(!callback.await(500, TimeUnit.MILLISECONDS), "close cancels document callback");
        }

        finally {
            synchronizer.close();
            scheduler.shutdownNow();
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
