package org.sqldeps.dbeaver.editor.index;

import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.List;

public final class IndexCatalogTest {
    private IndexCatalogTest() {
    }

    public static void main(String[] args) throws Exception {
        loadsSortedSqliteFilesAndMarksActiveIndex();
        ignoresInvalidActiveIndexPointers();
        formatsIndexMetadataForSelection();

        System.out.println("sqldeps: Java index catalog tests ok");
    }

    private static void loadsSortedSqliteFilesAndMarksActiveIndex() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-index-catalog-");
        String previousHome = System.getProperty("sqldeps.home");

        try {
            System.setProperty("sqldeps.home", directory.toString());
            Files.createFile(directory.resolve("zeta.sqlite"));
            Files.createFile(directory.resolve("Alpha.sqlite"));
            Files.createFile(directory.resolve("ignored.sql"));
            Files.writeString(directory.resolve("active-index"), directory.resolve("zeta.sqlite").toString());

            List<IndexCatalog.IndexEntry> entries = IndexCatalog.load();

            check(entries.size() == 2, "sqlite file filtering");
            check("Alpha".equals(entries.get(0).name()), "case-insensitive sorting");
            check(!entries.get(0).active(), "inactive index marker");
            check("zeta".equals(entries.get(1).name()), "extension removal");
            check(entries.get(1).active(), "active index marker");
        }

        finally {
            restoreHome(previousHome);
            deleteDirectory(directory);
        }
    }

    private static void ignoresInvalidActiveIndexPointers() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-index-catalog-");
        String previousHome = System.getProperty("sqldeps.home");

        try {
            System.setProperty("sqldeps.home", directory.toString());
            Files.createFile(directory.resolve("index.sqlite"));
            Files.writeString(directory.resolve("active-index"), "");

            List<IndexCatalog.IndexEntry> entries = IndexCatalog.load();

            check(entries.size() == 1, "index remains visible with invalid pointer");
            check(!entries.get(0).active(), "invalid pointer does not mark an index active");
        }

        finally {
            restoreHome(previousHome);
            deleteDirectory(directory);
        }
    }

    private static void formatsIndexMetadataForSelection() {
        IndexMetadata.Details metadata = new IndexMetadata.Details(
            "/tmp/sql-repo",
            12,
            34,
            Instant.parse("2026-11-07T10:15:30Z"),
            IndexMetadata.Freshness.CURRENT);
        IndexCatalog.IndexEntry entry = new IndexCatalog.IndexEntry(
            "sql-repo",
            "/tmp/sql-repo.sqlite",
            true,
            metadata);
        String description = entry.toString();

        check(description.contains("root: /tmp/sql-repo"), "source root description");
        check(description.contains("files: 12 · objects: 34"), "file and object counts");
        check(description.contains("status: current"), "freshness description");
        check(description.contains("(active)"), "active marker");
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
