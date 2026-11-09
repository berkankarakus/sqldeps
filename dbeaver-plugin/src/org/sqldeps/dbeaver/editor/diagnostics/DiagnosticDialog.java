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
package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.regex.Matcher;
import java.util.regex.Pattern;

import org.eclipse.jface.dialogs.Dialog;
import org.eclipse.jface.dialogs.IDialogConstants;
import org.eclipse.jface.resource.ColorRegistry;
import org.eclipse.jface.resource.JFaceResources;
import org.eclipse.swt.SWT;
import org.eclipse.swt.custom.StyleRange;
import org.eclipse.swt.custom.StyledText;
import org.eclipse.swt.graphics.Color;
import org.eclipse.swt.graphics.Font;
import org.eclipse.swt.graphics.RGB;
import org.eclipse.swt.layout.GridData;
import org.eclipse.swt.widgets.Composite;
import org.eclipse.swt.widgets.Control;
import org.eclipse.swt.widgets.Shell;

/** Displays the current diagnostics in a larger, resizable and severity-aware dialog. */
final class DiagnosticDialog extends Dialog {
    private static final int PREFERRED_WIDTH = 980;
    private static final int PREFERRED_HEIGHT = 600;
    private static final Pattern SEVERITY_PATTERN = Pattern.compile(
        "\\[(error|warning|info|hint)\\]",
        Pattern.CASE_INSENSITIVE
    );
    private static final String HEADER_COLOR_KEY = "org.sqldeps.dbeaver.diagnostics.header";
    private static final String ERROR_COLOR_KEY = "org.sqldeps.dbeaver.diagnostics.error";
    private static final String WARNING_COLOR_KEY = "org.sqldeps.dbeaver.diagnostics.warning";
    private static final String INFO_COLOR_KEY = "org.sqldeps.dbeaver.diagnostics.info";
    private static final RGB PASTEL_HEADER = new RGB(197, 213, 234);
    private static final RGB PASTEL_ERROR = new RGB(238, 189, 190);
    private static final RGB PASTEL_WARNING = new RGB(238, 218, 174);
    private static final RGB PASTEL_INFO = new RGB(190, 214, 232);

    private final String text;

    DiagnosticDialog(Shell parent, String text) {
        super(parent);
        this.text = text;
        setShellStyle(getShellStyle() | SWT.RESIZE | SWT.MAX);
    }

    @Override
    protected void configureShell(Shell shell) {
        super.configureShell(shell);
        shell.setText("sqldeps Diagnostics");
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
        layoutData.widthHint = PREFERRED_WIDTH;
        layoutData.heightHint = PREFERRED_HEIGHT;
        viewer.setLayoutData(layoutData);

        return area;
    }

    private void applyStyles(StyledText viewer) {
        ColorRegistry colorRegistry = JFaceResources.getColorRegistry();
        Font boldFont = JFaceResources.getFontRegistry().getBold(JFaceResources.DEFAULT_FONT);
        Color headerColor = color(colorRegistry, HEADER_COLOR_KEY, PASTEL_HEADER);

        int firstLineEnd = text.indexOf('\n');
        if (firstLineEnd < 0) {
            firstLineEnd = text.length();
        }

        applyStyle(viewer, 0, firstLineEnd, headerColor, boldFont);

        Matcher severityMatcher = SEVERITY_PATTERN.matcher(text);
        while (severityMatcher.find()) {
            applyStyle(
                viewer,
                severityMatcher.start(),
                severityMatcher.end() - severityMatcher.start(),
                severityColor(colorRegistry, severityMatcher.group(1)),
                boldFont
            );
        }
    }

    private static Color severityColor(ColorRegistry colorRegistry, String severity) {
        return switch (severity.toLowerCase()) {
            case "error" -> color(colorRegistry, ERROR_COLOR_KEY, PASTEL_ERROR);
            case "warning" -> color(colorRegistry, WARNING_COLOR_KEY, PASTEL_WARNING);
            default -> color(colorRegistry, INFO_COLOR_KEY, PASTEL_INFO);
        };
    }

    private static Color color(ColorRegistry colorRegistry, String key, RGB rgb) {
        Color color = colorRegistry.get(key);
        if (color != null) {
            return color;
        }

        colorRegistry.put(key, rgb);

        return colorRegistry.get(key);
    }

    private static void applyStyle(
        StyledText viewer,
        int start,
        int length,
        Color foreground,
        Font font
    ) {
        StyleRange style = new StyleRange();
        style.start = start;
        style.length = length;
        style.foreground = foreground;
        style.font = font;
        viewer.setStyleRange(style);
    }

    @Override
    protected void createButtonsForButtonBar(Composite parent) {
        createButton(parent, IDialogConstants.OK_ID, IDialogConstants.CLOSE_LABEL, true);
    }
}
