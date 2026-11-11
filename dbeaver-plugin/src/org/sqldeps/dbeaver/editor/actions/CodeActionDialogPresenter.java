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

import java.util.List;

import org.eclipse.jface.dialogs.MessageDialog;
import org.eclipse.jface.window.Window;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.swt.SWT;
import org.eclipse.swt.widgets.Shell;

/** Owns the selection dialog lifecycle without deciding how a selected action is executed. */
final class CodeActionDialogPresenter {
    private final Runnable previousDiagnostic;
    private final Runnable nextDiagnostic;

    CodeActionDialogPresenter(Runnable previousDiagnostic, Runnable nextDiagnostic) {
        this.previousDiagnostic = previousDiagnostic;
        this.nextDiagnostic = nextDiagnostic;
    }

    CodeActionChoice open(
        Shell shell,
        List<CodeActionChoice> choices,
        java.util.function.Consumer<String> traceConsumer)
    {
        CodeActionSelectionDialog dialog = new CodeActionSelectionDialog(
            shell,
            choices,
            previousDiagnostic,
            nextDiagnostic
        );
        dialog.setTitle("sqldeps code actions");
        dialog.create();

        Shell dialogShell = dialog.getShell();
        dialogShell.addListener(SWT.Show, event -> activate(dialogShell));

        traceConsumer.accept("opening code action dialog");
        int result = dialog.open();
        traceConsumer.accept("code action dialog closed with result " + result);

        return CodeActionSelection.confirmedChoice(
            result == Window.OK,
            dialog.getResult()
        );
    }

    static void showNoCodeActionMessage(Shell shell, List<Diagnostic> diagnostics) {
        String message = "No safe quick fix is available for this finding.";

        if (!diagnostics.isEmpty() && diagnostics.get(0).getMessage() != null) {
            message += "\n\nFinding:\n" + diagnostics.get(0).getMessage();
        }

        MessageDialog.openInformation(shell, "sqldeps Quick Fix", message);
    }

    private static void activate(Shell dialogShell) {
        if (dialogShell.isDisposed()) {
            return;
        }

        dialogShell.forceActive();
        dialogShell.forceFocus();
    }
}
