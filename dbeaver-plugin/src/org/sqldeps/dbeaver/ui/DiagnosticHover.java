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

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.source.IAnnotationHover;
import org.eclipse.jface.text.source.ISourceViewer;
import org.eclipse.jface.text.source.ISourceViewerExtension3;
import org.eclipse.lsp4j.Diagnostic;

final class DiagnosticHover implements IAnnotationHover {
    private IAnnotationHover delegate;
    private Map<Integer, List<String>> messagesByLine = Map.of();

    void install(ISourceViewer sourceViewer) {
        if (sourceViewer instanceof ISourceViewerExtension3 extension) {
            IAnnotationHover currentHover = extension.getCurrentAnnotationHover();
            if (currentHover != this) {
                delegate = currentHover;
            }
        }

        sourceViewer.setAnnotationHover(this);
    }

    void replace(List<Diagnostic> diagnostics) {
        Map<Integer, List<String>> replacements = new HashMap<>();
        for (Diagnostic diagnostic : diagnostics) {
            if (diagnostic == null || diagnostic.getRange() == null || diagnostic.getMessage() == null) {
                continue;
            }

            int line = diagnostic.getRange().getStart().getLine();
            replacements.computeIfAbsent(line, ignored -> new ArrayList<>()).add(diagnostic.getMessage());
        }

        messagesByLine = replacements;
    }

    @Override
    public String getHoverInfo(ISourceViewer sourceViewer, int lineNumber) {
        String diagnosticInfo = diagnosticInfo(lineNumber);
        String delegatedInfo = delegate == null || delegate == this
            ? null
            : delegate.getHoverInfo(sourceViewer, lineNumber);

        if (diagnosticInfo == null) {
            return delegatedInfo;
        }

        if (delegatedInfo == null || delegatedInfo.isBlank()) {
            return diagnosticInfo;
        }

        return diagnosticInfo + System.lineSeparator() + System.lineSeparator() + delegatedInfo;
    }

    private String diagnosticInfo(int lineNumber) {
        List<String> messages = messagesByLine.get(lineNumber);
        if (messages == null || messages.isEmpty()) {
            return null;
        }

        return String.join(System.lineSeparator(), messages);
    }
}
