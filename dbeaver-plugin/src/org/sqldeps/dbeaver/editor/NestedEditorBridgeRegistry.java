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
package org.sqldeps.dbeaver.editor;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.WeakHashMap;

import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;

/** Owns nested-editor bridge lookup and active-editor tracking. */
final class NestedEditorBridgeRegistry {
    private final Map<SQLEditorNested<?>, NestedSqlEditorLspBridge> bridges = new WeakHashMap<>();
    private NestedSqlEditorLspBridge activeBridge;

    synchronized NestedSqlEditorLspBridge forEditor(SQLEditorNested<?> editor) {
        return bridges.get(editor);
    }

    synchronized void register(
        SQLEditorNested<?> editor,
        NestedSqlEditorLspBridge bridge
    ) {
        bridges.put(editor, bridge);
        activeBridge = bridge;
    }

    synchronized NestedSqlEditorLspBridge remove(SQLEditorNested<?> editor) {
        return bridges.remove(editor);
    }

    synchronized boolean isActive(NestedSqlEditorLspBridge bridge) {
        return activeBridge == bridge;
    }

    synchronized NestedSqlEditorLspBridge active() {
        return activeBridge;
    }

    synchronized void clearActive() {
        activeBridge = null;
    }

    synchronized List<NestedSqlEditorLspBridge> snapshot() {
        return new ArrayList<>(bridges.values());
    }
}
