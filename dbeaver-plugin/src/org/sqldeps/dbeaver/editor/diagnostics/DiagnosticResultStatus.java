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
import org.eclipse.lsp4j.jsonrpc.messages.Either;

/** Classifies diagnostic results for the DBeaver status line. */
public final class DiagnosticResultStatus {
    private DiagnosticResultStatus() {
    }

    /** Returns the user-facing status label for one diagnostic response. */
    public static String describe(List<Diagnostic> diagnostics) {
        if (hasStaleIndex(diagnostics)) {
            return "stale index — " + diagnostics.size() + " finding(s)";
        }

        if (hasPartialAnalysis(diagnostics)) {
            return "partial analysis — " + diagnostics.size() + " finding(s)";
        }

        if (diagnostics.isEmpty()) {
            return "up to date — no findings";
        }

        return "up to date — " + diagnostics.size() + " finding(s)";
    }

    /** Returns whether at least one diagnostic reports a stale index. */
    public static boolean hasStaleIndex(List<Diagnostic> diagnostics) {
        return diagnostics.stream().anyMatch(DiagnosticResultStatus::isStaleIndex);
    }

    private static boolean hasPartialAnalysis(List<Diagnostic> diagnostics) {
        return diagnostics.stream().anyMatch(DiagnosticResultStatus::isPartialAnalysis);
    }

    private static boolean isPartialAnalysis(Diagnostic diagnostic) {
        Either<String, Integer> code = diagnostic.getCode();

        return code != null && code.isLeft() && "partial-analysis".equals(code.getLeft());
    }

    private static boolean isStaleIndex(Diagnostic diagnostic) {
        if ("stale-index".equals(diagnostic.getSource())) {
            return true;
        }

        return hasStaleMetadata(diagnostic.getData());
    }

    private static boolean hasStaleMetadata(Object data) {
        if (!(data instanceof Map<?, ?> metadata)) {
            return false;
        }

        return Boolean.TRUE.equals(metadata.get("stale"))
            || "stale-index".equals(metadata.get("source"));
    }
}
