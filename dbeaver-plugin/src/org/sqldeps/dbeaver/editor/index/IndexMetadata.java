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
package org.sqldeps.dbeaver.editor.index;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Instant;
import java.util.stream.Stream;

/** Reads optional display metadata from an sqldeps SQLite index. */
final class IndexMetadata {
    private IndexMetadata() {
    }

    static Details load(Path databasePath) {
        Instant updatedAt = lastModifiedAt(databasePath);

        try (Connection connection = open(databasePath);
             Statement statement = connection.createStatement();
             ResultSet result = statement.executeQuery(
                 "SELECT "
                     + "(SELECT value FROM meta WHERE key = 'source_root'), "
                     + "(SELECT COUNT(*) FROM files), "
                     + "(SELECT COUNT(*) FROM objects)")) {
            result.next();
            String sourceRoot = result.getString(1);
            long fileCount = result.getLong(2);
            long objectCount = result.getLong(3);

            return new Details(
                sourceRoot,
                fileCount,
                objectCount,
                updatedAt,
                freshness(sourceRoot, updatedAt));
        }

        catch (SQLException | ClassNotFoundException exception) {
            return Details.unknown(updatedAt);
        }
    }

    private static Connection open(Path databasePath) throws SQLException, ClassNotFoundException {
        Class.forName("org.sqlite.JDBC");

        return DriverManager.getConnection("jdbc:sqlite:" + databasePath.toAbsolutePath());
    }

    private static Instant lastModifiedAt(Path databasePath) {
        try {
            return Files.getLastModifiedTime(databasePath).toInstant();
        }

        catch (IOException exception) {
            return Instant.EPOCH;
        }
    }

    private static Freshness freshness(String sourceRoot, Instant updatedAt) {
        Path sourceDirectory = sourceDirectory(sourceRoot);
        if (sourceDirectory == null) {
            return Freshness.UNKNOWN;
        }

        try (Stream<Path> paths = Files.walk(sourceDirectory)) {
            boolean sourceChanged = paths
                .filter(Files::isRegularFile)
                .filter(IndexMetadata::isSqlFile)
                .map(IndexMetadata::lastModifiedAt)
                .anyMatch(sourceTime -> sourceTime.isAfter(updatedAt));

            return sourceChanged ? Freshness.STALE : Freshness.CURRENT;
        }

        catch (IOException | RuntimeException exception) {
            return Freshness.UNKNOWN;
        }
    }

    private static Path sourceDirectory(String sourceRoot) {
        if (sourceRoot == null || sourceRoot.isBlank()) {
            return null;
        }

        Path path = Path.of(sourceRoot);
        return Files.isDirectory(path) ? path : null;
    }

    private static boolean isSqlFile(Path path) {
        String fileName = path.getFileName().toString();

        return fileName.endsWith(".sql") || fileName.endsWith(".SQL");
    }

    private static String formatCount(long count) {
        return count < 0 ? "unknown" : Long.toString(count);
    }

    record Details(
        String sourceRoot,
        long fileCount,
        long objectCount,
        Instant updatedAt,
        Freshness freshness
    ) {
        static Details unknown(Instant updatedAt) {
            return new Details(null, -1, -1, updatedAt, Freshness.UNKNOWN);
        }

        String describe(String name, String path, boolean active) {
            StringBuilder text = new StringBuilder(name);
            if (active) {
                text.append(" (active)");
            }

            text.append("\nroot: ").append(sourceRoot == null ? "unknown" : sourceRoot);
            text.append("\nfiles: ").append(formatCount(fileCount));
            text.append(" · objects: ").append(formatCount(objectCount));
            text.append("\nupdated: ").append(updatedAt);
            text.append(" · status: ").append(freshness.label());
            text.append("\n").append(path);

            return text.toString();
        }
    }

    enum Freshness {
        CURRENT("current"),
        STALE("stale"),
        UNKNOWN("unknown");

        private final String label;

        Freshness(String label) {
            this.label = label;
        }

        String label() {
            return label;
        }
    }
}
