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

import java.util.List;
import java.util.Map;
import java.util.WeakHashMap;

import org.jkiss.dbeaver.ui.editors.sql.SQLEditor;

/** Owns the weak, synchronized registry of active SQL editor add-ins. */
final class SqlEditorAddInRegistry {
    private final Map<SQLEditor, SqlEditorLspAddIn> addIns = new WeakHashMap<>();

    synchronized void register(SQLEditor editor, SqlEditorLspAddIn addIn) {
        addIns.put(editor, addIn);
    }

    synchronized void unregister(SQLEditor editor) {
        addIns.remove(editor);
    }

    synchronized SqlEditorLspAddIn forEditor(SQLEditor editor) {
        return addIns.get(editor);
    }

    synchronized List<SqlEditorLspAddIn> snapshot() {
        return List.copyOf(addIns.values());
    }
}
