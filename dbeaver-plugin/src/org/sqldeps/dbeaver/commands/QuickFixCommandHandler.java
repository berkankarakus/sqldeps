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
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.handlers.HandlerUtil;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;

/** Bridges DBeaver's quick-fix command to the range-aware sqldeps code actions. */
public final class QuickFixCommandHandler extends AbstractHandler {
    @Override
    public Object execute(ExecutionEvent event) {
        IEditorPart editorPart = HandlerUtil.getActiveEditor(event);
        ISelection selection = HandlerUtil.getCurrentSelection(event);
        if (!(editorPart instanceof SQLEditorBase editor)
            || !(selection instanceof ITextSelection textSelection)) {
            return null;
        }

        int offset = textSelection.getOffset();
        int length = textSelection.getLength();
        if (SqlEditorLspAddIn.showCodeActionsFor(editor, offset, length)) {
            return null;
        }

        NestedSqlEditorLspBridge.showCodeActionsFor(editor, offset, length);
        return null;
    }
}
