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

import org.eclipse.jface.dialogs.IDialogConstants;
import org.eclipse.jface.viewers.ArrayContentProvider;
import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.jface.viewers.StructuredSelection;
import org.eclipse.jface.viewers.TableViewer;
import org.eclipse.swt.SWT;
import org.eclipse.swt.events.KeyAdapter;
import org.eclipse.swt.events.KeyEvent;
import org.eclipse.swt.layout.GridData;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Control;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Listener;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.swt.widgets.Text;
import org.eclipse.ui.dialogs.ListDialog;

/**
 * Lists the code actions in the order they were built (ElementListSelectionDialog sorts its list alphabetically by
 * label, which would put the real fixes below context and maintenance actions). The first action is preselected.
 */
final class CodeActionSelectionDialog extends ListDialog {
    private static final int LIST_WIDTH_IN_CHARACTERS = 110;
    private static final int LIST_HEIGHT_IN_ROWS = 16;
    private static final int PREVIOUS_ID = IDialogConstants.CLIENT_ID + 1;
    private static final int NEXT_ID = IDialogConstants.CLIENT_ID + 2;

    private final Runnable previousAction;
    private final Runnable nextAction;
    private final List<CodeActionChoice> choices;
    private Text filterText;

    CodeActionSelectionDialog(
        Shell shell,
        List<CodeActionChoice> choices,
        Runnable previousAction,
        Runnable nextAction
    ) {
        super(shell);
        this.previousAction = previousAction;
        this.nextAction = nextAction;
        this.choices = choices;
        setContentProvider(ArrayContentProvider.getInstance());
        setLabelProvider(new CodeActionLabelProvider());
        setInput(choices);
        setMessage("Select a code action. Down: list, Ctrl/Cmd+F: search, Enter: apply, Esc: cancel.");
        setInitialSelections(choices.get(0));
        setWidthInChars(LIST_WIDTH_IN_CHARACTERS);
        setHeightInChars(LIST_HEIGHT_IN_ROWS);
    }

    @Override
    protected Control createDialogArea(Composite parent) {
        Composite area = (Composite) super.createDialogArea(parent);
        TableViewer viewer = getTableViewer();

        filterText = new Text(area, SWT.SEARCH | SWT.ICON_SEARCH | SWT.CANCEL);
        filterText.setMessage("Filter code actions");
        filterText.setLayoutData(new GridData(SWT.FILL, SWT.CENTER, true, false));
        filterText.moveAbove(viewer.getTable());
        filterText.addModifyListener(event -> updateFilter());
        filterText.addKeyListener(new KeyAdapter() {
            @Override
            public void keyPressed(KeyEvent event) {
                if (event.keyCode == SWT.ARROW_DOWN && viewer.getTable().getItemCount() > 0) {
                    viewer.getTable().setFocus();
                    event.doit = false;
                }
            }
        });
        Display display = filterText.getDisplay();
        Shell dialogShell = getShell();
        Listener searchShortcut = event -> {
            if (!(event.widget instanceof Control control) || control.getShell() != dialogShell) {
                return;
            }

            if ((event.stateMask & SWT.MOD1) != 0 && event.keyCode == 'f') {
                filterText.setFocus();
                filterText.selectAll();
                event.doit = false;
            }
        };
        display.addFilter(SWT.KeyDown, searchShortcut);
        dialogShell.addDisposeListener(event -> display.removeFilter(SWT.KeyDown, searchShortcut));
        filterText.getDisplay().asyncExec(() -> {
            if (!filterText.isDisposed()) {
                filterText.setFocus();
            }
        });

        return area;
    }

    private void updateFilter() {
        TableViewer viewer = getTableViewer();
        Object selected = viewer.getStructuredSelection().getFirstElement();
        List<CodeActionChoice> matches = CodeActionSelection.filter(choices, filterText.getText());
        viewer.setInput(matches);

        if (matches.isEmpty()) {
            viewer.setSelection(StructuredSelection.EMPTY);
        }
        else if (matches.contains(selected)) {
            viewer.setSelection(new StructuredSelection(selected));
        }
        else {
            viewer.setSelection(new StructuredSelection(matches.get(0)));
        }

        getButton(IDialogConstants.OK_ID).setEnabled(!matches.isEmpty());
    }

    @Override
    protected void createButtonsForButtonBar(Composite parent) {
        createButton(parent, PREVIOUS_ID, "‹ Previous finding", false);
        createButton(parent, NEXT_ID, "Next finding ›", false);
        super.createButtonsForButtonBar(parent);
    }

    @Override
    protected void buttonPressed(int buttonId) {
        if (buttonId == PREVIOUS_ID) {
            previousAction.run();
            close();

            return;
        }

        if (buttonId == NEXT_ID) {
            nextAction.run();
            close();

            return;
        }

        super.buttonPressed(buttonId);
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
