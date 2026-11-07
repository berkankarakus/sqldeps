package org.sqldeps.dbeaver.editor.index;

import java.util.prefs.Preferences;

public final class IndexSelectionRestoreTest {
    private IndexSelectionRestoreTest() {
    }

    public static void main(String[] args) {
        Preferences preferences = Preferences.userNodeForPackage(IndexSelectionState.class);
        preferences.put("selectedIndexPath", "/tmp/sqldeps-index-that-no-longer-exists.sqlite");

        check(IndexSelectionState.automaticIndexSelection(), "missing saved index restores automatic mode");
        check(IndexSelectionState.restoredMissingSelection(), "missing saved index is reported");
        check(
            IndexStatus.describe().contains("saved index unavailable; using automatic selection"),
            "status explains automatic fallback"
        );

        IndexSelectionState.useAutomaticSelection();

        System.out.println("sqldeps: Java index restore tests ok");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
