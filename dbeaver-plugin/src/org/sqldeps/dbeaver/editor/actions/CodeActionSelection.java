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

import java.util.List;

/** Decides how code action choices are presented and which one a dialog result confirms; free of SWT. */
final class CodeActionSelection {
    enum Mode {
        NONE,
        APPLY_DIRECTLY,
        ASK_USER
    }

    private CodeActionSelection() {
    }

    static Mode modeFor(List<CodeActionChoice> choices) {
        if (choices == null || choices.isEmpty()) {
            return Mode.NONE;
        }

        if (choices.size() == 1) {
            return Mode.APPLY_DIRECTLY;
        }

        return Mode.ASK_USER;
    }

    /** Returns the confirmed choice, or null when the dialog was cancelled or returned nothing usable. */
    static CodeActionChoice confirmedChoice(boolean confirmed, Object[] selectedItems) {
        if (!confirmed || selectedItems == null || selectedItems.length == 0) {
            return null;
        }

        if (selectedItems[0] instanceof CodeActionChoice choice) {
            return choice;
        }

        return null;
    }
}
