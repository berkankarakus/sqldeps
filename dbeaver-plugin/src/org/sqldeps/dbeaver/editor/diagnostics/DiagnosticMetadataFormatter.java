/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.List;
import java.util.Map;

import org.eclipse.lsp4j.Diagnostic;

/** Formats evidence-backed diagnostic metadata for the read-only diagnostics dialog. */
final class DiagnosticMetadataFormatter {
    private DiagnosticMetadataFormatter() {
    }

    static String format(Diagnostic diagnostic) {
        if (!(diagnostic.getData() instanceof Map<?, ?> metadata)) {
            return "";
        }

        StringBuilder details = new StringBuilder();
        appendField(details, "Analysis", metadata.get("source"));
        appendField(details, "Confidence", metadata.get("confidence"));

        if (metadata.get("stale") instanceof Boolean stale) {
            appendField(details, "Status", stale ? "stale" : "current");
        }

        if (metadata.get("evidence") instanceof Map<?, ?> evidence) {
            appendField(details, "Evidence", evidence.get("summary"));
            appendField(details, "Guidance", evidence.get("guidance"));
            appendReferences(details, evidence.get("references"));
        }

        return details.toString();
    }

    private static void appendReferences(StringBuilder details, Object value) {
        if (!(value instanceof List<?> references)) {
            return;
        }

        for (Object reference : references) {
            if (!(reference instanceof Map<?, ?> referenceMap)) {
                continue;
            }

            String title = text(referenceMap.get("title"));
            String url = text(referenceMap.get("url"));
            if (title.isBlank() && url.isBlank()) {
                continue;
            }

            String referenceText = title.isBlank() ? url : title;
            if (!url.isBlank() && !url.equals(referenceText)) {
                referenceText += " (" + url + ")";
            }

            appendField(details, "Reference", referenceText);
        }
    }

    private static void appendField(StringBuilder details, String label, Object value) {
        String text = text(value);
        if (text.isBlank()) {
            return;
        }

        details.append("   ")
            .append(label)
            .append(": ")
            .append(text)
            .append('\n');
    }

    private static String text(Object value) {
        return value instanceof String text ? text.trim() : "";
    }
}
