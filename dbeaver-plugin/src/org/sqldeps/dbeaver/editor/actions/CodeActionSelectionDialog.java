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

import java.util.Comparator;
import java.util.List;

import org.eclipse.core.runtime.CoreException;
import org.eclipse.core.runtime.IProgressMonitor;
import org.eclipse.core.runtime.IStatus;
import org.eclipse.core.runtime.Status;
import org.eclipse.jface.dialogs.DialogSettings;
import org.eclipse.jface.dialogs.IDialogSettings;
import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.dialogs.FilteredItemsSelectionDialog;

final class CodeActionSelectionDialog extends FilteredItemsSelectionDialog {
    private static final IDialogSettings DIALOG_SETTINGS = new DialogSettings("sqldeps.codeActions");

    private final List<CodeActionChoice> choices;

    CodeActionSelectionDialog(Shell shell, List<CodeActionChoice> choices) {
        super(shell);
        this.choices = choices;
        setListLabelProvider(new LabelProvider() {
            @Override
            public String getText(Object element) {
                return ((CodeActionChoice) element).title();
            }
        });
    }

    @Override
    protected Composite createExtendedContentArea(Composite parent) {
        return null;
    }

    @Override
    protected IDialogSettings getDialogSettings() {
        return DIALOG_SETTINGS;
    }

    @Override
    protected IStatus validateItem(Object item) {
        return Status.OK_STATUS;
    }

    @Override
    protected ItemsFilter createFilter() {
        return new ItemsFilter() {
            @Override
            public boolean matchItem(Object item) {
                return matches(getElementName(item));
            }

            @Override
            public boolean isConsistentItem(Object item) {
                return true;
            }
        };
    }

    @Override
    protected Comparator<CodeActionChoice> getItemsComparator() {
        return Comparator.comparing(
            this::getElementName,
            String.CASE_INSENSITIVE_ORDER
        );
    }

    @Override
    protected void fillContentProvider(
        AbstractContentProvider contentProvider,
        ItemsFilter filter,
        IProgressMonitor monitor
    ) throws CoreException {
        for (CodeActionChoice choice : choices) {
            if (filter.matchItem(choice)) {
                contentProvider.add(choice, filter);
            }
        }
    }

    @Override
    public String getElementName(Object item) {
        return ((CodeActionChoice) item).title();
    }
}
