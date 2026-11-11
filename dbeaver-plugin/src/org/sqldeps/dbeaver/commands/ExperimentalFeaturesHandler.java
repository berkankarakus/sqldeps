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
import org.sqldeps.dbeaver.editor.actions.ExperimentalSuggestionsState;

/** Shows the experimental feature families and their safety boundary. */
public final class ExperimentalFeaturesHandler extends AbstractHandler {
    @Override
    public Object execute(ExecutionEvent event) throws ExecutionException {
        var shell = HandlerUtil.getActiveShell(event);
        if (shell == null || shell.isDisposed()) {
            return null;
        }

        MessageDialog.openInformation(
            shell,
            "sqldeps Experimental Features",
            ExperimentalSuggestionsState.featuresExplanation());

        return null;
    }
}
