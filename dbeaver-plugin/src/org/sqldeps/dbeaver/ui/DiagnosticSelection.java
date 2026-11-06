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

import java.util.List;

import org.eclipse.lsp4j.Diagnostic;

/** Keeps deterministic navigation state for the currently accepted diagnostics. */
final class DiagnosticSelection {
    private List<Diagnostic> diagnostics = List.of();
    private int currentIndex = -1;

    void replace(List<Diagnostic> diagnostics) {
        this.diagnostics = List.copyOf(diagnostics);
        currentIndex = -1;
    }

    List<Diagnostic> current() {
        return diagnostics;
    }

    Diagnostic first() {
        return selectAt(0);
    }

    Diagnostic next() {
        return selectRelative(1);
    }

    Diagnostic previous() {
        return selectRelative(-1);
    }

    private Diagnostic selectRelative(int direction) {
        if (diagnostics.isEmpty()) {
            return null;
        }

        int targetIndex = currentIndex < 0
            ? direction > 0 ? 0 : diagnostics.size() - 1
            : Math.floorMod(currentIndex + direction, diagnostics.size());
        return selectAt(targetIndex);
    }

    private Diagnostic selectAt(int index) {
        if (index < 0 || index >= diagnostics.size()) {
            return null;
        }

        currentIndex = index;
        return diagnostics.get(index);
    }
}
