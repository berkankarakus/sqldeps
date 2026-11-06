/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.sqldeps.dbeaver.ui;

/** Stores the opt-in state for experimental code actions and code generation. */
public final class ExperimentalSuggestionsState {
    private static final String SYSTEM_PROPERTY = "sqldeps.lsp.experimentalSuggestions";

    private static volatile boolean enabled = Boolean.getBoolean(SYSTEM_PROPERTY);

    private ExperimentalSuggestionsState() {
    }

    /** Returns whether newly started LSP sessions request experimental suggestions. */
    public static boolean isEnabled() {
        return enabled;
    }

    /** Updates the opt-in state for subsequently started or restarted LSP sessions. */
    public static void setEnabled(boolean value) {
        enabled = value;
    }
}
