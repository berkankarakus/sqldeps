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

/** Read-only, resizable and scrollable viewer for long command results. */
final class TextResultDialog extends StyledTextDialog {
    private static final int PREFERRED_WIDTH = 760;
    private static final int PREFERRED_HEIGHT = 460;
    private static final Pattern LABEL_PATTERN = Pattern.compile("(?m)^([A-Za-z ]+):");
    private static final Pattern QUALIFIED_NAME_PATTERN = Pattern.compile(
        "(?<![A-Za-z0-9_/.])(?:[A-Za-z_][A-Za-z0-9_$]*\\.)+[A-Za-z_][A-Za-z0-9_$]*"
    );
    private static final Pattern BRACKETED_IDENTIFIER_PATTERN = Pattern.compile("\\[[^\\]\\r\\n]+\\]");
    private static final Pattern ANNOTATION_PATTERN = Pattern.compile("\\([^)]*\\)");
    private static final String TITLE_COLOR_KEY = "org.sqldeps.dbeaver.result.title";
    private static final String OBJECT_COLOR_KEY = "org.sqldeps.dbeaver.result.object";
    private static final String ANNOTATION_COLOR_KEY = "org.sqldeps.dbeaver.result.annotation";
    private static final RGB PASTEL_TITLE = new RGB(197, 213, 234);
    private static final RGB PASTEL_OBJECT = new RGB(191, 220, 203);
    private static final RGB PASTEL_ANNOTATION = new RGB(216, 200, 232);
    private static final RGB DARK_PASTEL_TITLE = new RGB(159, 190, 230);
    private static final RGB DARK_PASTEL_OBJECT = new RGB(143, 205, 169);
    private static final RGB DARK_PASTEL_ANNOTATION = new RGB(192, 160, 223);

    TextResultDialog(Shell parent, String title, String text) {
        super(parent, title, text, PREFERRED_WIDTH, PREFERRED_HEIGHT);
    }

    @Override
    protected void applyStyles(StyledText viewer) {
        if (!isStructuredResult(text)) {
            return;
        }

        Color titleColor = themeColor(TITLE_COLOR_KEY, PASTEL_TITLE, DARK_PASTEL_TITLE);
        Color objectColor = themeColor(OBJECT_COLOR_KEY, PASTEL_OBJECT, DARK_PASTEL_OBJECT);
        Color annotationColor = themeColor(
            ANNOTATION_COLOR_KEY,
            PASTEL_ANNOTATION,
            DARK_PASTEL_ANNOTATION
        );
        Font boldFont = JFaceResources.getFontRegistry().getBold(JFaceResources.DEFAULT_FONT);

        int firstLineEnd = text.indexOf('\n');
        if (firstLineEnd < 0) {
            firstLineEnd = text.length();
        }

        applyStyle(viewer, 0, firstLineEnd, titleColor, boldFont);

        Matcher labelMatcher = LABEL_PATTERN.matcher(text);
        while (labelMatcher.find()) {
            applyStyle(
                viewer,
                labelMatcher.start(1),
                labelMatcher.end(1) - labelMatcher.start(1),
                titleColor,
                boldFont
            );
        }

        Matcher nameMatcher = QUALIFIED_NAME_PATTERN.matcher(text);
        while (nameMatcher.find()) {
            applyStyle(viewer, nameMatcher.start(), nameMatcher.end() - nameMatcher.start(), objectColor, null);
        }

        Matcher bracketedIdentifierMatcher = BRACKETED_IDENTIFIER_PATTERN.matcher(text);
        while (bracketedIdentifierMatcher.find()) {
            applyStyle(
                viewer,
                bracketedIdentifierMatcher.start(),
                bracketedIdentifierMatcher.end() - bracketedIdentifierMatcher.start(),
                objectColor,
                null
            );
        }

        Matcher annotationMatcher = ANNOTATION_PATTERN.matcher(text);
        while (annotationMatcher.find()) {
            applyStyle(
                viewer,
                annotationMatcher.start(),
                annotationMatcher.end() - annotationMatcher.start(),
                annotationColor,
                null
            );
        }
    }

    private static boolean isStructuredResult(String value) {
        return value.startsWith("Repository context:")
            || value.startsWith("Impact of changing:")
            || value.startsWith("CREATE INDEX ");
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
