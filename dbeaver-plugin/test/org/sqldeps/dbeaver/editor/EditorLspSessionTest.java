package org.sqldeps.dbeaver.editor;

import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

public final class EditorLspSessionTest {
    private EditorLspSessionTest() {
    }

    public static void main(String[] args) {
        ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
        EditorLspSession session = new EditorLspSession(
            null,
            null,
            "file:///tmp/sqldeps-session.sql",
            null,
            scheduler,
            "diagnostics",
            message -> {
            }
        );

        try {
            session.start();
            session.restart();
            session.close();
        }

        finally {
            scheduler.shutdownNow();
        }

        System.out.println("sqldeps: editor LSP readiness tests ok");
    }
}
