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

import org.eclipse.jface.resource.JFaceResources;
import org.eclipse.swt.custom.StyleRange;
import org.eclipse.swt.custom.StyledText;
import org.eclipse.swt.graphics.Color;
import org.eclipse.swt.graphics.Font;
import org.eclipse.swt.graphics.RGB;
import org.eclipse.swt.widgets.Shell;
import org.sqldeps.dbeaver.editor.StyledTextDialog;

/** Displays the current diagnostics in a larger, resizable and severity-aware dialog. */
final class DiagnosticDialog extends StyledTextDialog {
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
    private static final RGB DARK_PASTEL_HEADER = new RGB(159, 190, 230);
    private static final RGB DARK_PASTEL_ERROR = new RGB(238, 145, 151);
    private static final RGB DARK_PASTEL_WARNING = new RGB(238, 198, 104);
    private static final RGB DARK_PASTEL_INFO = new RGB(144, 194, 232);

    DiagnosticDialog(Shell parent, String text) {
        super(parent, "sqldeps Diagnostics", text, PREFERRED_WIDTH, PREFERRED_HEIGHT);
    }

    @Override
    protected void applyStyles(StyledText viewer) {
        Font boldFont = JFaceResources.getFontRegistry().getBold(JFaceResources.DEFAULT_FONT);
        Color headerColor = themeColor(HEADER_COLOR_KEY, PASTEL_HEADER, DARK_PASTEL_HEADER);

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
                severityColor(severityMatcher.group(1)),
                boldFont
            );
        }
    }

    private static Color severityColor(String severity) {
        return switch (severity.toLowerCase()) {
            case "error" -> themeColor(ERROR_COLOR_KEY, PASTEL_ERROR, DARK_PASTEL_ERROR);
            case "warning" -> themeColor(WARNING_COLOR_KEY, PASTEL_WARNING, DARK_PASTEL_WARNING);
            default -> themeColor(INFO_COLOR_KEY, PASTEL_INFO, DARK_PASTEL_INFO);
        };
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

}
