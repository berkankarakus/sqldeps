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

import org.eclipse.core.resources.IFile;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.lsp.DocumentUri;

/** Resolves the stable LSP document and workspace context for a DBeaver editor. */
final class EditorDocumentContext {
    private EditorDocumentContext() {
    }

    static String documentUri(SQLEditorBase editor, String unsavedPrefix) {
        IFile file = editor.getEditorInput().getAdapter(IFile.class);
        if (file != null && file.getLocationURI() != null) {
            return file.getLocationURI().normalize().toString();
        }

        return DocumentUri.forUnsavedEditor(
            unsavedPrefix + Integer.toHexString(System.identityHashCode(editor))
        );
    }

    static String workspaceUri(SQLEditorBase editor) {
        IFile file = editor.getEditorInput().getAdapter(IFile.class);
        if (file == null || file.getProject() == null || file.getProject().getLocationURI() == null) {
            return null;
        }

        return file.getProject().getLocationURI().normalize().toString();
    }
}
