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
package org.sqldeps.dbeaver.ui;

import java.nio.file.Path;
import java.util.List;

import org.eclipse.jface.viewers.LabelProvider;
import org.eclipse.swt.SWT;
import org.eclipse.swt.widgets.FileDialog;
import org.eclipse.swt.widgets.Shell;
import org.eclipse.ui.dialogs.ElementListSelectionDialog;

public final class IndexSelector {
    private IndexSelector() {
    }

    public static String choose(Shell shell) {
        List<IndexCatalog.IndexEntry> indexes = IndexCatalog.load();
        if (!indexes.isEmpty()) {
            ElementListSelectionDialog dialog = new ElementListSelectionDialog(shell, new LabelProvider());
            dialog.setTitle("Select sqldeps Index");
            dialog.setMessage("Select a registered sqldeps index:");
            dialog.setElements(indexes.toArray());

            if (dialog.open() != ElementListSelectionDialog.OK) {
                return null;
            }

            Object result = dialog.getFirstResult();
            if (result instanceof IndexCatalog.IndexEntry index) {
                return index.path();
            }

            return null;
        }

        FileDialog dialog = new FileDialog(shell, SWT.OPEN);
        dialog.setText("Select sqldeps index");
        dialog.setFilterExtensions(new String[] { "*.sqlite", "*" });
        String selectedPath = dialog.open();
        if (selectedPath == null || selectedPath.isBlank()) {
            return null;
        }

        return Path.of(selectedPath).toAbsolutePath().normalize().toString();
    }
}
