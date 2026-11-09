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
package org.sqldeps.dbeaver.commands;

import org.eclipse.core.commands.AbstractHandler;
import org.eclipse.core.commands.ExecutionEvent;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.handlers.HandlerUtil;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditor;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;

/** Formats the active SQL script or nested source editor document. */
public final class FormatHandler extends AbstractHandler {
    @Override
    public Object execute(ExecutionEvent event) {
        IEditorPart editorPart = HandlerUtil.getActiveEditor(event);
        if (editorPart instanceof SQLEditor editor) {
            SqlEditorLspAddIn addIn = SqlEditorLspAddIn.forEditor(editor);
            if (addIn != null) {
                addIn.formatScript();
            }
        }

        else if (editorPart instanceof SQLEditorNested<?> editor) {
            NestedSqlEditorLspBridge bridge = NestedSqlEditorLspBridge.forEditor(editor);
            if (bridge != null) {
                bridge.formatScript();
            }
        }

        else if (NestedSqlEditorLspBridge.active() != null) {
            NestedSqlEditorLspBridge.active().formatScript();
        }

        return null;
    }
}

