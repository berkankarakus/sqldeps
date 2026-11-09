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

import java.util.List;

import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.dialogs.ElementListSelectionDialog;

/** Synchronous list dialog; elements are filled before the shell opens, with the first one preselected. */
final class CodeActionSelectionDialog extends ElementListSelectionDialog {
    private static final int LIST_WIDTH_IN_CHARACTERS = 110;
    private static final int LIST_HEIGHT_IN_ROWS = 16;

    CodeActionSelectionDialog(Shell shell, List<CodeActionChoice> choices) {
        super(shell, new CodeActionLabelProvider());
        setElements(choices.toArray());
        setMultipleSelection(false);
        setMessage("Select a code action");
        setInitialSelections(choices.get(0));
        setSize(LIST_WIDTH_IN_CHARACTERS, LIST_HEIGHT_IN_ROWS);
    }

    @Override
    protected boolean isResizable() {
        return true;
    }

    private static final class CodeActionLabelProvider extends LabelProvider {
        @Override
        public String getText(Object element) {
            return ((CodeActionChoice) element).title();
        }
    }
}
