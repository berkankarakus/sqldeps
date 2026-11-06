package org.sqldeps.dbeaver.editor.index;

public final class IndexSelectionStateTest {
    private IndexSelectionStateTest() {
    }

    public static void main(String[] args) {
        selectDisablesAutomaticSelection();
        automaticSelectionClearsExplicitPath();

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

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
