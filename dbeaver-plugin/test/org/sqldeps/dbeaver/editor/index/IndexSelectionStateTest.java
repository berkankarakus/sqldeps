package org.sqldeps.dbeaver.editor.index;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.prefs.Preferences;

public final class IndexSelectionStateTest {
    private IndexSelectionStateTest() {
    }

    public static void main(String[] args) {
        IndexSelectionState.useAutomaticSelection();

        check(IndexSelectionState.automaticIndexSelection(), "automatic selection is the default");
        selectDisablesAutomaticSelection();
        automaticSelectionClearsExplicitPath();

        try {
            persistsExplicitSelection();
        }

        catch (Exception exception) {
            throw new AssertionError("Failed: persists explicit selection", exception);
        }

        IndexSelectionState.useAutomaticSelection();

        System.out.println("sqldeps: Java index selection tests ok");
    }

    private static void selectDisablesAutomaticSelection() {
        IndexSelectionState.select("/tmp/project.sqlite");

        check("/tmp/project.sqlite".equals(IndexSelectionState.selectedIndexPath()), "selected index path");
        check(!IndexSelectionState.automaticIndexSelection(), "manual selection mode");
    }

    private static void automaticSelectionClearsExplicitPath() {
        IndexSelectionState.select("/tmp/project.sqlite");

        IndexSelectionState.useAutomaticSelection();

        check(IndexSelectionState.selectedIndexPath() == null, "automatic selection clears path");
        check(IndexSelectionState.automaticIndexSelection(), "automatic selection mode");
    }

    private static void persistsExplicitSelection() throws Exception {
        Path index = Files.createTempFile("sqldeps-selection-", ".sqlite");

        try {
            IndexSelectionState.select(index.toString());

            String persistedPath = Preferences.userNodeForPackage(IndexSelectionState.class)
                .get("selectedIndexPath", null);

            check(index.toAbsolutePath().normalize().toString().equals(persistedPath), "persisted index path");
        }

        finally {
            Files.deleteIfExists(index);
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
