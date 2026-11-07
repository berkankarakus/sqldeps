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

import java.nio.file.Files;
import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.util.prefs.Preferences;

/** Stores the user's explicit or automatic SQLite index selection mode. */
public final class IndexSelectionState {
    private static final String PREFERENCE_KEY = "selectedIndexPath";
    private static final Preferences PREFERENCES = Preferences.userNodeForPackage(IndexSelectionState.class);
    private static volatile boolean restoredMissingSelection;
    private static volatile String selectedIndexPath = loadSelectedIndexPath();
    private static volatile boolean automaticIndexSelection = selectedIndexPath == null;

    private IndexSelectionState() {
    }

    /** Returns the explicitly selected index path, or {@code null} in automatic mode. */
    public static String selectedIndexPath() {
        return selectedIndexPath;
    }

    /** Returns whether sessions should resolve an index from workspace context. */
    public static boolean automaticIndexSelection() {
        return automaticIndexSelection;
    }

    /** Returns whether a persisted path was unavailable and automatic mode was restored. */
    public static boolean restoredMissingSelection() {
        return restoredMissingSelection;
    }

    /** Selects a concrete SQLite index for subsequently started or restarted sessions. */
    public static void select(String indexPath) {
        selectedIndexPath = normalizePath(indexPath);
        automaticIndexSelection = false;
        restoredMissingSelection = false;
        persistSelectedIndexPath(selectedIndexPath);
    }

    /** Clears the explicit path and restores automatic workspace selection. */
    public static void useAutomaticSelection() {
        selectedIndexPath = null;
        automaticIndexSelection = true;
        restoredMissingSelection = false;
        removePersistedSelection();
    }

    private static String loadSelectedIndexPath() {
        String path;

        try {
            path = PREFERENCES.get(PREFERENCE_KEY, null);
        }

        catch (SecurityException exception) {
            return null;
        }

        if (path == null || path.isBlank()) {
            return null;
        }

        String normalizedPath = normalizePath(path);
        if (normalizedPath == null || !Files.isRegularFile(Path.of(normalizedPath))) {
            restoredMissingSelection = true;
            removePersistedSelection();
            return null;
        }

        return normalizedPath;
    }

    private static String normalizePath(String path) {
        if (path == null || path.isBlank()) {
            return null;
        }

        try {
            return Path.of(path).toAbsolutePath().normalize().toString();
        }

        catch (InvalidPathException exception) {
            return null;
        }
    }

    private static void persistSelectedIndexPath(String path) {
        if (path == null) {
            removePersistedSelection();
            return;
        }

        try {
            PREFERENCES.put(PREFERENCE_KEY, path);
        }

        catch (SecurityException exception) {
            // In-memory selection remains useful when the host blocks preference writes.
        }
    }

    private static void removePersistedSelection() {
        try {
            PREFERENCES.remove(PREFERENCE_KEY);
        }

        catch (SecurityException exception) {
            // Automatic mode is still applied in memory when preference removal is blocked.
        }
    }
}
