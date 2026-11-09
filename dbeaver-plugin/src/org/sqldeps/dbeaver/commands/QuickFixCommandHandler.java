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

import java.util.Iterator;

import org.eclipse.core.commands.AbstractHandler;
import org.eclipse.core.commands.ExecutionEvent;
import org.eclipse.jface.text.ITextSelection;
import org.eclipse.jface.text.Position;
import org.eclipse.jface.viewers.ISelection;
import org.eclipse.jface.text.source.IAnnotationModel;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.handlers.HandlerUtil;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.TraceSupport;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;
import org.sqldeps.dbeaver.editor.diagnostics.SqldepsProblemAnnotation;

/** Bridges DBeaver's quick-fix command to the range-aware sqldeps code actions. */
public final class QuickFixCommandHandler extends AbstractHandler {
    @Override
    public Object execute(ExecutionEvent event) {
        IEditorPart editorPart = HandlerUtil.getActiveEditor(event);
        ISelection selection = HandlerUtil.getCurrentSelection(event);
        trace("quick fix command invoked");
        SQLEditorBase editor;
        if (editorPart instanceof SQLEditorBase sqlEditor) {
            editor = sqlEditor;
        }

        else if (editorPart != null) {
            editor = editorPart.getAdapter(SQLEditorBase.class);
        }

        else {
            editor = null;
        }

        if (editor == null) {
            trace("active editor is not a DBeaver SQL editor");
            return null;
        }

        int offset = 0;
        int length = 0;
        if (selection instanceof ITextSelection textSelection) {
            offset = textSelection.getOffset();
            length = textSelection.getLength();
        }

        Position diagnosticPosition = findDiagnosticPosition(editor, offset);
        if (diagnosticPosition != null) {
            offset = diagnosticPosition.getOffset();
            length = diagnosticPosition.getLength();
            trace("matched sqldeps diagnostic at offset " + offset + ", length " + length);
        }

        if (!(selection instanceof ITextSelection) && diagnosticPosition == null) {
            trace("no text selection or sqldeps diagnostic was found");
            return null;
        }

        if (SqlEditorLspAddIn.showCodeActionsFor(editor, offset, length)) {
            trace("routed quick fix to the SQL editor LSP session");
            return null;
        }

        trace("SQL editor session was not available; trying nested editor bridge");
        boolean routed = NestedSqlEditorLspBridge.showCodeActionsFor(editor, offset, length);
        trace(routed
            ? "routed quick fix to the nested editor LSP session"
            : "nested editor LSP session was not available");
        return null;
    }

    private static void trace(String message) {
        TraceSupport.log(null, message);
    }

    private static Position findDiagnosticPosition(SQLEditorBase editor, int selectionOffset) {
        IAnnotationModel model = editor.getAnnotationModel();
        if (model == null) {
            return null;
        }

        Iterator<?> annotations = model.getAnnotationIterator();
        while (annotations.hasNext()) {
            Object value = annotations.next();
            if (!(value instanceof SqldepsProblemAnnotation annotation)) {
                continue;
            }

            Position position = model.getPosition(annotation);
            if (position != null && contains(position, selectionOffset)) {
                return position;
            }
        }

        return null;
    }

    private static boolean contains(Position position, int offset) {
        int start = position.getOffset();
        int end = start + Math.max(1, position.getLength());

        return offset >= start && offset <= end;
    }
}
