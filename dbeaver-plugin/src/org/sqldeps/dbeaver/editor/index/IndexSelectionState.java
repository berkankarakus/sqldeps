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

/** Stores the user's explicit or automatic SQLite index selection mode. */
public final class IndexSelectionState {
    private static volatile String selectedIndexPath;
    private static volatile boolean automaticIndexSelection = true;

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

    /** Selects a concrete SQLite index for subsequently started or restarted sessions. */
    public static void select(String indexPath) {
        selectedIndexPath = indexPath;
        automaticIndexSelection = false;
    }

    /** Clears the explicit path and restores automatic workspace selection. */
    public static void useAutomaticSelection() {
        selectedIndexPath = null;
        automaticIndexSelection = true;
    }
}
