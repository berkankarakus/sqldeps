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

import org.eclipse.lsp4j.DiagnosticSeverity;

/** Formats LSP diagnostic severities consistently across DBeaver views. */
public final class DiagnosticSeverityName {
    private DiagnosticSeverityName() {
    }

    /** Returns the stable lowercase label shown in DBeaver summaries and action lists. */
    public static String of(DiagnosticSeverity severity) {
        if (severity == null) {
            return "diagnostic";
        }

        return switch (severity) {
            case Error -> "error";
            case Warning -> "warning";
            case Information -> "info";
            case Hint -> "hint";
            default -> "diagnostic";
        };
    }
}
