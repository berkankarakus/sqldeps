package org.sqldeps.dbeaver.lsp;

public final class DocumentStateTest {
    private DocumentStateTest() {
    }

    public static void main(String[] args) {
        initialSnapshotPreservesDocumentState();
        updateAdvancesVersionAndGeneration();
        staleSnapshotsAreRejected();
        currentSnapshotsAreAccepted();

        System.out.println("sqldeps: Java document state tests ok");
    }

    private static void initialSnapshotPreservesDocumentState() {
        DocumentState state = new DocumentState("file:///tmp/query.sql", "SELECT 1;");

        DocumentSnapshot snapshot = state.snapshot();

        check("file:///tmp/query.sql".equals(snapshot.uri()), "initial URI");
        check(snapshot.version() == 1, "initial version");
        check(snapshot.generation() == 1, "initial generation");
        check("SELECT 1;".equals(snapshot.text()), "initial text");
    }

    private static void updateAdvancesVersionAndGeneration() {
        DocumentState state = new DocumentState("file:///tmp/query.sql", "SELECT 1;");

        DocumentSnapshot updated = state.update("SELECT 2;");

        check(updated.version() == 2, "updated version");
        check(updated.generation() == 2, "updated generation");
        check("SELECT 2;".equals(updated.text()), "updated text");
    }

    private static void staleSnapshotsAreRejected() {
        DocumentState state = new DocumentState("file:///tmp/query.sql", "SELECT 1;");
        DocumentSnapshot initial = state.snapshot();

        state.update("SELECT 2;");

        check(!state.isCurrent(initial), "stale snapshot rejection");
    }

    private static void currentSnapshotsAreAccepted() {
        DocumentState state = new DocumentState("file:///tmp/query.sql", "SELECT 1;");
        DocumentSnapshot updated = state.update("SELECT 2;");

        check(state.isCurrent(updated), "current snapshot acceptance");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
