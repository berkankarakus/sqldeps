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
import java.nio.file.Path;

/** Formats the index selection state for editor status messages. */
public final class IndexStatus {
    private IndexStatus() {
    }

    /** Returns a compact label that identifies the index mode and selected index. */
    public static String describe() {
        if (IndexSelectionState.automaticIndexSelection()) {
            return automaticDescription();
        }

        String selectedPath = IndexSelectionState.selectedIndexPath();
        if (selectedPath == null || selectedPath.isBlank()) {
            return "index: none (manual)";
        }

        if (!Files.isRegularFile(Path.of(selectedPath))) {
            return "index: missing (manual)";
        }

        return "index: " + indexName(selectedPath) + " (manual)";
    }

    /** Returns whether the current selection cannot provide an index to the LSP session. */
    public static boolean requiresSelection() {
        if (IndexSelectionState.automaticIndexSelection()) {
            return IndexCatalog.load().stream().noneMatch(IndexCatalog.IndexEntry::active);
        }

        String selectedPath = IndexSelectionState.selectedIndexPath();
        return selectedPath == null
            || selectedPath.isBlank()
            || !Files.isRegularFile(Path.of(selectedPath));
    }

    private static String automaticDescription() {
        for (IndexCatalog.IndexEntry entry : IndexCatalog.load()) {
            if (entry.active()) {
                return "index: " + entry.name() + " (automatic)";
            }
        }

        return "index: none (automatic)";
    }

    private static String indexName(String path) {
        String fileName = Path.of(path).getFileName().toString();
        if (fileName.endsWith(".sqlite")) {
            return fileName.substring(0, fileName.length() - ".sqlite".length());
        }

        return fileName;
    }
}
