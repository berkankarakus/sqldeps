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

import org.eclipse.jface.text.contentassist.ICompletionProposal;
import org.eclipse.jface.text.quickassist.IQuickAssistInvocationContext;
import org.eclipse.jface.text.quickassist.IQuickAssistProcessor;
import org.eclipse.jface.text.source.Annotation;
import org.eclipse.jface.text.source.ISourceViewer;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.IWorkbenchPage;
import org.eclipse.ui.IWorkbenchWindow;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;
import org.sqldeps.dbeaver.editor.diagnostics.SqldepsProblemAnnotation;

/** Opens range-specific sqldeps code actions directly from DBeaver's quick-fix UI. */
public final class SqldepsQuickFixProcessor implements IQuickAssistProcessor {
    @Override
    public boolean canFix(Annotation annotation) {
        return annotation instanceof SqldepsProblemAnnotation;
    }

    @Override
    public boolean canAssist(IQuickAssistInvocationContext invocationContext) {
        return false;
    }

    @Override
    public ICompletionProposal[] computeQuickAssistProposals(
        IQuickAssistInvocationContext invocationContext
    ) {
        ISourceViewer sourceViewer = invocationContext.getSourceViewer();
        if (sourceViewer == null) {
            return new ICompletionProposal[0];
        }

        SQLEditorBase editor = activeSqlEditor();
        if (editor == null) {
            return new ICompletionProposal[0];
        }

        showCodeActions(
            editor,
            invocationContext.getOffset(),
            invocationContext.getLength()
        );

        return new ICompletionProposal[0];
    }

    @Override
    public String getErrorMessage() {
        return null;
    }

    private static boolean showCodeActions(SQLEditorBase editor, int offset, int length) {
        if (SqlEditorLspAddIn.showCodeActionsFor(editor, offset, length)) {
            return true;
        }

        return NestedSqlEditorLspBridge.showCodeActionsFor(editor, offset, length);
    }

    private static SQLEditorBase activeSqlEditor() {
        if (!PlatformUI.isWorkbenchRunning()) {
            return null;
        }

        IWorkbenchWindow window = PlatformUI.getWorkbench().getActiveWorkbenchWindow();
        if (window == null) {
            return null;
        }

        IWorkbenchPage page = window.getActivePage();
        if (page == null) {
            return null;
        }

        IEditorPart editor = page.getActiveEditor();
        if (editor instanceof SQLEditorBase sqlEditor) {
            return sqlEditor;
        }

        return editor == null ? null : editor.getAdapter(SQLEditorBase.class);
    }

}
