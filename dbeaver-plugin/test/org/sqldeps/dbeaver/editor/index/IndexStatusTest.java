package org.sqldeps.dbeaver.editor.index;

import java.nio.file.Files;
import java.nio.file.Path;

public final class IndexStatusTest {
    private IndexStatusTest() {
    }

    public static void main(String[] args) throws Exception {
        reportsManualIndexName();
        reportsMissingManualIndex();
        reportsAutomaticActiveIndex();
        reportsMissingAutomaticIndex();

        System.out.println("sqldeps: Java index status tests ok");
    }

    private static void reportsManualIndexName() throws Exception {
        Path index = Files.createTempFile("sqldeps-project-", ".sqlite");

        try {
            IndexSelectionState.select(index.toString());

            check(
                ("index: " + index.getFileName().toString().replace(".sqlite", "") + " (manual)")
                    .equals(IndexStatus.describe()),
                "manual index name"
            );
        }

        finally {
            Files.deleteIfExists(index);
        }
    }

    private static void reportsAutomaticActiveIndex() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-index-status-");
        String previousHome = System.getProperty("sqldeps.home");

        try {
            Path activeIndex = directory.resolve("project.sqlite");
            Files.createFile(activeIndex);
            Files.writeString(directory.resolve("active-index"), activeIndex.toString());
            System.setProperty("sqldeps.home", directory.toString());
            IndexSelectionState.useAutomaticSelection();

            check(
                "index: project (automatic)".equals(IndexStatus.describe()),
                "automatic active index"
            );
        }

        finally {
            restoreHome(previousHome);
            deleteDirectory(directory);
        }
    }

    private static void reportsMissingManualIndex() {
        IndexSelectionState.select("/tmp/missing-project.sqlite");

        check(
            "index: missing (manual)".equals(IndexStatus.describe()),
            "missing manual index"
        );
    }

    private static void reportsMissingAutomaticIndex() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-index-status-");
        String previousHome = System.getProperty("sqldeps.home");

        try {
            System.setProperty("sqldeps.home", directory.toString());
            IndexSelectionState.useAutomaticSelection();

            check(
                "index: none (automatic)".equals(IndexStatus.describe()),
                "missing automatic index"
            );
        }

        finally {
            restoreHome(previousHome);
            deleteDirectory(directory);
        }
    }

    private static void restoreHome(String previousHome) {
        if (previousHome == null) {
            System.clearProperty("sqldeps.home");
            return;
        }

        System.setProperty("sqldeps.home", previousHome);
    }

    private static void deleteDirectory(Path directory) throws Exception {
        try (var paths = Files.walk(directory)) {
            paths.sorted((left, right) -> right.compareTo(left)).forEach(path -> {
                try {
                    Files.deleteIfExists(path);
                }

                catch (Exception exception) {
                    throw new RuntimeException(exception);
                }
            });
        }
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
