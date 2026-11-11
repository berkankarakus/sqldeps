/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.sqldeps.dbeaver.editor.actions;

import java.util.prefs.Preferences;

/** Stores the opt-in state for experimental code actions and code generation. */
public final class ExperimentalSuggestionsState {
    private static final String SYSTEM_PROPERTY = "sqldeps.lsp.experimentalSuggestions";
    private static final String PREFERENCE_KEY = "enabled";
    private static final Preferences PREFERENCES = Preferences.userNodeForPackage(ExperimentalSuggestionsState.class);

    private static volatile boolean enabled = load();

    private ExperimentalSuggestionsState() {
    }

    /** Returns whether newly started LSP sessions request experimental suggestions. */
    public static boolean isEnabled() {
        return enabled;
    }

    /** Updates the opt-in state for subsequently started or restarted LSP sessions. */
    public static void setEnabled(boolean value) {
        enabled = value;
        PREFERENCES.putBoolean(PREFERENCE_KEY, value);
    }

    /** Explains the per-user scope, safety boundary, and restart effect of this setting. */
    public static String explanation(boolean value) {
        String state = value ? "enable" : "disable";
        return "This will " + state + " experimental sqldeps suggestions for this DBeaver user profile. "
            + "Suggestions are preview-only and never write files automatically. "
            + "Active SQL editor sessions restart so the new setting takes effect.";
    }

    /** Describes the experimental feature families shown in the DBeaver menu. */
    public static String featuresExplanation() {
        return "Experimental sqldeps features:\n\n"
            + "• Code generation: expand SELECT * and complete missing procedure parameters.\n"
            + "• Safe rewrites: offer guarded WHERE templates, type casts and ORDER BY fixes.\n"
            + "• Read-only previews: query builder and dependency-aware suggestions.\n\n"
            + "All edits are previewed as WorkspaceEdit proposals and require an explicit action. "
            + "No files are written automatically. Enabling this setting restarts active SQL editor sessions.";
    }

    private static boolean load() {
        return PREFERENCES.getBoolean(PREFERENCE_KEY, Boolean.getBoolean(SYSTEM_PROPERTY));
    }
}
