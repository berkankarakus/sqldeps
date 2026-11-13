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
package org.sqldeps.dbeaver.editor.actions;

import java.util.Map;
import java.util.Set;

/** Describes the server commands that the DBeaver adapter can display or execute. */
final class SqldepsCommands {
    private static final Map<String, String> CATEGORIES = Map.ofEntries(
        Map.entry("sqldeps.query-builder", "Preview"),
        Map.entry("sqldeps.index-suggestion", "Preview"),
        Map.entry("sqldeps.repository-context", "Context"),
        Map.entry("sqldeps.who-writes", "Context"),
        Map.entry("sqldeps.who-reads", "Context"),
        Map.entry("sqldeps.impact", "Context"),
        Map.entry("sqldeps.callers", "Context"),
        Map.entry("sqldeps.column-writers", "Context"),
        Map.entry("sqldeps.explain-diagnostic", "Explain"),
        Map.entry("sqldeps.reindex", "Maintenance")
    );
    private static final Set<String> PREVIEW_COMMANDS = Set.of(
        "sqldeps.query-builder",
        "sqldeps.index-suggestion",
        "sqldeps.explain-diagnostic",
        "sqldeps.repository-context",
        "sqldeps.who-writes",
        "sqldeps.who-reads",
        "sqldeps.impact",
        "sqldeps.callers",
        "sqldeps.column-writers"
    );

    private SqldepsCommands() {
    }

    /** Returns every server command understood by the adapter. */
    static Set<String> all() {
        return CATEGORIES.keySet();
    }

    /** Returns whether a server command is safe for the adapter to display. */
    static boolean isKnown(String command) {
        return command != null && CATEGORIES.containsKey(command);
    }

    /** Returns whether a command result should be shown as read-only preview text. */
    static boolean isPreview(String command) {
        return command != null && PREVIEW_COMMANDS.contains(command);
    }

    /** Returns the user-facing category for a command. */
    static String category(String command) {
        return CATEGORIES.getOrDefault(command, "Action");
    }
}
