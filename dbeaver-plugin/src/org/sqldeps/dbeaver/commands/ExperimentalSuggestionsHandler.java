/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.sqldeps.dbeaver.commands;

import org.eclipse.core.commands.AbstractHandler;
import org.eclipse.core.commands.ExecutionEvent;
import org.eclipse.core.commands.ExecutionException;
import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.ui.handlers.HandlerUtil;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;
import org.sqldeps.dbeaver.editor.actions.ExperimentalSuggestionsState;

/** Toggles experimental code actions and restarts active SQL editor sessions. */
public final class ExperimentalSuggestionsHandler extends AbstractHandler {
    @Override
    public Object execute(ExecutionEvent event) throws ExecutionException {
        boolean enabled = !ExperimentalSuggestionsState.isEnabled();
        if (!confirmChange(event, enabled)) {
            return null;
        }

        event.getCommand().getState("org.eclipse.ui.commands.toggleState").setValue(enabled);
        ExperimentalSuggestionsState.setEnabled(enabled);
        SqlEditorLspAddIn.restartAll();
        NestedSqlEditorLspBridge.restartAll();

        return null;
    }

    private static boolean confirmChange(ExecutionEvent event, boolean enabled) {
        var shell = HandlerUtil.getActiveShell(event);
        if (shell == null || shell.isDisposed()) {
            return true;
        }

        return MessageDialog.openQuestion(
            shell,
            "sqldeps Experimental Suggestions",
            ExperimentalSuggestionsState.explanation(enabled) + "\n\nContinue?");
    }
}
