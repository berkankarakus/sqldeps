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
import org.eclipse.jface.text.IDocument;
import org.eclipse.jface.text.contentassist.IContextInformation;
import org.eclipse.jface.text.quickassist.IQuickAssistInvocationContext;
import org.eclipse.jface.text.quickassist.IQuickAssistProcessor;
import org.eclipse.jface.text.source.Annotation;
import org.eclipse.jface.text.source.ISourceViewer;
import org.eclipse.swt.graphics.Image;
import org.eclipse.swt.graphics.Point;
import org.eclipse.ui.IEditorPart;
import org.eclipse.ui.IWorkbenchPage;
import org.eclipse.ui.IWorkbenchWindow;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;
import org.sqldeps.dbeaver.editor.diagnostics.SqldepsProblemAnnotation;

/** Exposes range-specific sqldeps code actions through DBeaver's quick-fix UI. */
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

        return new ICompletionProposal[] {
            new CodeActionProposal(
                editor,
                invocationContext.getOffset(),
                invocationContext.getLength()
            )
        };
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
        return editor instanceof SQLEditorBase sqlEditor ? sqlEditor : null;
    }

    private static final class CodeActionProposal implements ICompletionProposal {
        private final SQLEditorBase editor;
        private final int offset;
        private final int length;

        private CodeActionProposal(SQLEditorBase editor, int offset, int length) {
            this.editor = editor;
            this.offset = offset;
            this.length = length;
        }

        @Override
        public void apply(IDocument document) {
            showCodeActions(editor, offset, length);
        }

        @Override
        public Point getSelection(IDocument document) {
            return new Point(offset, 0);
        }

        @Override
        public String getAdditionalProposalInfo() {
            return "Yalnızca bu sqldeps bulgusuna ait quick fix'leri göster";
        }

        @Override
        public String getDisplayString() {
            return "Show quick fixes for this sqldeps finding";
        }

        @Override
        public IContextInformation getContextInformation() {
            return null;
        }

        @Override
        public Image getImage() {
            return null;
        }
    }
}
