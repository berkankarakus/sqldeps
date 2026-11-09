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
package org.sqldeps.dbeaver.editor.index;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Set;
import java.util.TreeSet;

import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.swt.SWT;
import org.eclipse.swt.layout.GridData;
import org.eclipse.swt.layout.GridLayout;
import org.eclipse.swt.widgets.Button;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Label;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.swt.widgets.Text;

/** Provides a small DBeaver UI for repository-level sqldeps suppressions. */
public final class RepositorySuppressionDialog {
    private RepositorySuppressionDialog() {
    }

    /** Opens the suppression editor and returns whether the file was changed. */
    public static boolean open(Shell parent, Path repositoryRoot) {
        Set<String> initialRules;
        try {
            initialRules = RepositorySuppressionStore.read(repositoryRoot);
        }

        catch (IOException exception) {
            MessageDialog.openError(parent, "sqldeps Suppressions", exception.getMessage());
            return false;
        }

        Shell dialog = new Shell(parent, SWT.DIALOG_TRIM | SWT.APPLICATION_MODAL | SWT.RESIZE);
        dialog.setText("sqldeps Repository Suppressions");
        dialog.setLayout(new GridLayout(2, false));

        Label description = new Label(dialog, SWT.WRAP);
        description.setText(
            "Rules disabled for this indexed repository. Remove a rule to enable it again."
                + " Changes are written to .sqldeps."
        );
        GridData descriptionData = new GridData(SWT.FILL, SWT.TOP, true, false, 2, 1);
        descriptionData.widthHint = 520;
        description.setLayoutData(descriptionData);

        org.eclipse.swt.widgets.List ruleList = new org.eclipse.swt.widgets.List(
            dialog,
            SWT.BORDER | SWT.SINGLE | SWT.V_SCROLL
        );
        ruleList.setLayoutData(new GridData(SWT.FILL, SWT.FILL, true, true));
        for (String rule : initialRules) {
            ruleList.add(rule);
        }

        Composite buttons = new Composite(dialog, SWT.NONE);
        buttons.setLayoutData(new GridData(SWT.FILL, SWT.TOP, false, true));
        buttons.setLayout(new GridLayout(1, false));

        Text ruleInput = new Text(buttons, SWT.BORDER);
        ruleInput.setMessage("rule name");
        ruleInput.setLayoutData(new GridData(150, SWT.DEFAULT));

        Button addButton = new Button(buttons, SWT.PUSH);
        addButton.setText("Add");
        addButton.setLayoutData(new GridData(SWT.FILL, SWT.TOP, true, false));
        addButton.addListener(SWT.Selection, event -> addRule(dialog, ruleInput, ruleList));

        Button removeButton = new Button(buttons, SWT.PUSH);
        removeButton.setText("Remove");
        removeButton.setLayoutData(new GridData(SWT.FILL, SWT.TOP, true, false));
        removeButton.addListener(SWT.Selection, event -> removeSelectedRule(ruleList));

        Composite footer = new Composite(dialog, SWT.NONE);
        footer.setLayoutData(new GridData(SWT.END, SWT.BOTTOM, true, false, 2, 1));
        footer.setLayout(new GridLayout(2, true));

        Button cancelButton = new Button(footer, SWT.PUSH);
        cancelButton.setText("Cancel");
        cancelButton.setLayoutData(new GridData(SWT.FILL, SWT.CENTER, true, false));
        cancelButton.addListener(SWT.Selection, event -> dialog.close());

        Button saveButton = new Button(footer, SWT.PUSH);
        saveButton.setText("Save");
        saveButton.setLayoutData(new GridData(SWT.FILL, SWT.CENTER, true, false));
        final boolean[] changed = { false };
        saveButton.addListener(SWT.Selection, event -> {
            Set<String> rules = new TreeSet<>();
            for (String rule : ruleList.getItems()) {
                rules.add(rule);
            }

            try {
                RepositorySuppressionStore.write(repositoryRoot, rules);
                changed[0] = !rules.equals(initialRules);
                dialog.close();
            }

            catch (IOException | IllegalArgumentException exception) {
                MessageDialog.openError(dialog, "sqldeps Suppressions", exception.getMessage());
            }
        });

        dialog.setDefaultButton(saveButton);
        dialog.setSize(620, 420);
        dialog.open();
        while (!dialog.isDisposed()) {
            if (!dialog.getDisplay().readAndDispatch()) {
                dialog.getDisplay().sleep();
            }
        }

        return changed[0];
    }

    private static void addRule(
        Shell dialog,
        Text ruleInput,
        org.eclipse.swt.widgets.List ruleList
    ) {
        String rule = RepositorySuppressionStore.normalizeRule(ruleInput.getText());
        if (!RepositorySuppressionStore.isValidRule(rule)) {
            MessageDialog.openWarning(dialog, "sqldeps Suppressions", "Enter a rule name such as nolock.");
            return;
        }

        for (String existingRule : ruleList.getItems()) {
            if (existingRule.equals(rule)) {
                ruleInput.selectAll();
                return;
            }
        }

        ruleList.add(rule);
        ruleList.setSelection(ruleList.indexOf(rule));
        ruleInput.setText("");
    }

    private static void removeSelectedRule(org.eclipse.swt.widgets.List ruleList) {
        int selectionIndex = ruleList.getSelectionIndex();
        if (selectionIndex >= 0) {
            ruleList.remove(selectionIndex);
        }
    }
}
