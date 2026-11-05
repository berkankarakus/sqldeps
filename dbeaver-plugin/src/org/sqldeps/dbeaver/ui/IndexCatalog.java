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
package org.sqldeps.dbeaver.ui;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.List;
import java.util.Objects;
import java.util.stream.Stream;

/** Discovers named SQLite indexes registered by the sqldeps CLI. */
public final class IndexCatalog {
    private IndexCatalog() {
    }

    /** Returns readable index entries, marking the active pointer when it is valid. */
    public static List<IndexEntry> load() {
        Path directory = indexDirectory();
        if (!Files.isDirectory(directory)) {
            return List.of();
        }

        Path activePath = activeIndexPath(directory);
        try (Stream<Path> paths = Files.list(directory)) {
            return paths
                .filter(Files::isRegularFile)
                .filter(path -> path.getFileName().toString().endsWith(".sqlite"))
                .map(path -> path.toAbsolutePath().normalize())
                .sorted(Comparator.comparing(path -> path.getFileName().toString(), String.CASE_INSENSITIVE_ORDER))
                .map(path -> new IndexEntry(
                    removeExtension(path.getFileName().toString()),
                    path.toString(),
                    path.equals(activePath)
                ))
                .toList();
        }

        catch (IOException exception) {
            return List.of();
        }
    }

    private static Path indexDirectory() {
        String configuredHome = System.getProperty("sqldeps.home");
        if (configuredHome == null || configuredHome.isBlank()) {
            configuredHome = System.getenv("SQLDEPS_HOME");
        }

        if (configuredHome == null || configuredHome.isBlank()) {
            return Path.of(
                System.getProperty("user.home"),
                ".local",
                "share",
                "sqldeps"
            );
        }

        return Path.of(configuredHome).toAbsolutePath().normalize();
    }

    private static Path activeIndexPath(Path directory) {
        Path pointerPath = directory.resolve("active-index");
        if (!Files.isRegularFile(pointerPath)) {
            return null;
        }

        try {
            String value = Files.readString(pointerPath).trim();
            if (value.isBlank()) {
                return null;
            }

            return Path.of(value).toAbsolutePath().normalize();
        }

        catch (IOException | RuntimeException exception) {
            return null;
        }
    }

    private static String removeExtension(String fileName) {
        return fileName.substring(0, fileName.length() - ".sqlite".length());
    }

    public record IndexEntry(String name, String path, boolean active) {
        public IndexEntry {
            Objects.requireNonNull(name);
            Objects.requireNonNull(path);
        }

        @Override
        public String toString() {
            if (active) {
                return name + " (active)\n" + path;
            }

            return name + "\n" + path;
        }
    }
}
