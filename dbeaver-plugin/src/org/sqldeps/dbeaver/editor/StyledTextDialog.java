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

import org.eclipse.jface.dialogs.Dialog;
import org.eclipse.jface.dialogs.IDialogConstants;
import org.eclipse.jface.resource.ColorRegistry;
import org.eclipse.jface.resource.JFaceResources;
import org.eclipse.swt.SWT;
import org.eclipse.swt.custom.StyledText;
import org.eclipse.swt.graphics.Color;
import org.eclipse.swt.graphics.RGB;
import org.eclipse.swt.layout.GridData;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Control;
import org.eclipse.swt.widgets.Display;
import org.eclipse.swt.widgets.Shell;

/** Provides the common read-only, scrollable dialog shell used by result viewers. */
public abstract class StyledTextDialog extends Dialog {
    private final String title;
    private final int preferredWidth;
    private final int preferredHeight;
    protected final String text;

    protected StyledTextDialog(
        Shell parent,
        String title,
        String text,
        int preferredWidth,
        int preferredHeight
    ) {
        super(parent);
        this.title = title;
        this.text = text;
        this.preferredWidth = preferredWidth;
        this.preferredHeight = preferredHeight;
        setShellStyle(getShellStyle() | SWT.RESIZE | SWT.MAX);
    }

    @Override
    protected void configureShell(Shell shell) {
        super.configureShell(shell);
        shell.setText(title);
    }

    @Override
    protected boolean isResizable() {
        return true;
    }

    @Override
    protected Control createDialogArea(Composite parent) {
        Composite area = (Composite) super.createDialogArea(parent);
        StyledText viewer = new StyledText(
            area,
            SWT.MULTI | SWT.READ_ONLY | SWT.BORDER | SWT.V_SCROLL | SWT.H_SCROLL
        );
        viewer.setFont(JFaceResources.getTextFont());
        viewer.setText(text);
        applyStyles(viewer);

        GridData layoutData = new GridData(SWT.FILL, SWT.FILL, true, true);
        layoutData.widthHint = preferredWidth;
        layoutData.heightHint = preferredHeight;
        viewer.setLayoutData(layoutData);

        return area;
    }

    /** Applies the dialog-specific styles to the populated viewer. */
    protected abstract void applyStyles(StyledText viewer);

    /** Returns a cached pastel color adjusted for the active light or dark display theme. */
    protected static Color themeColor(String key, RGB light, RGB dark) {
        ColorRegistry colorRegistry = JFaceResources.getColorRegistry();
        String themeKey = key + (isDarkTheme() ? ".dark" : ".light");
        Color color = colorRegistry.get(themeKey);
        if (color != null) {
            return color;
        }

        colorRegistry.put(themeKey, isDarkTheme() ? dark : light);

        return colorRegistry.get(themeKey);
    }

    private static boolean isDarkTheme() {
        RGB background = Display.getDefault().getSystemColor(SWT.COLOR_LIST_BACKGROUND).getRGB();
        int luminance = (background.red * 299 + background.green * 587 + background.blue * 114) / 1000;

        return luminance < 128;
    }

    @Override
    protected void createButtonsForButtonBar(Composite parent) {
        createButton(parent, IDialogConstants.OK_ID, IDialogConstants.CLOSE_LABEL, true);
    }
}
