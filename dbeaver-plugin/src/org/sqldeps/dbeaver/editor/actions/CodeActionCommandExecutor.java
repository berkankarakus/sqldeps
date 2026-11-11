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

import java.util.function.BooleanSupplier;
import java.util.function.Consumer;

import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.swt.widgets.Display;
import org.eclipse.ui.PlatformUI;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;

/** Applies edits and executes validated code-action commands for the current document. */
final class CodeActionCommandExecutor {
    private final DocumentSynchronizer synchronizer;
    private final BooleanSupplier activeSupplier;
    private final WorkspaceEditApplier workspaceEditApplier;
    private final Runnable previousDiagnostic;
    private final Runnable nextDiagnostic;
    private final Consumer<String> logConsumer;

    CodeActionCommandExecutor(
        DocumentSynchronizer synchronizer,
        BooleanSupplier activeSupplier,
        WorkspaceEditApplier workspaceEditApplier,
        Runnable previousDiagnostic,
        Runnable nextDiagnostic,
        Consumer<String> logConsumer)
    {
        this.synchronizer = synchronizer;
        this.activeSupplier = activeSupplier;
        this.workspaceEditApplier = workspaceEditApplier;
        this.previousDiagnostic = previousDiagnostic;
        this.nextDiagnostic = nextDiagnostic;
        this.logConsumer = logConsumer;
    }

    void applyWorkspaceEdit(DocumentSnapshot snapshot, String actionTitle, WorkspaceEdit edit) {
        workspaceEditApplier.apply(snapshot, actionTitle, edit);
    }

    void execute(DocumentSnapshot snapshot, LspConnection processManager, Command command) {
        if (!activeSupplier.getAsBoolean() || !synchronizer.isCurrent(snapshot) || !isSupported(command)) {
            logConsumer.accept("sqldeps rejected an unsupported or stale code action command");

            return;
        }

        processManager.executeCommand(command).whenComplete((result, exception) -> {
            if (exception != null) {
                logConsumer.accept("sqldeps code action command failed: " + exception.getMessage());

                return;
            }

            if (!SqldepsCommands.isPreview(command.getCommand()) || !(result instanceof String sql)) {
                return;
            }

            Display display = Display.getDefault();
            if (display.isDisposed()) {
                return;
            }

            display.asyncExec(() -> showPreview(snapshot, command, sql));
        });
    }

    private void showPreview(DocumentSnapshot snapshot, Command command, String sql) {
        if (!activeSupplier.getAsBoolean() || !synchronizer.isCurrent(snapshot)) {
            return;
        }

        var window = PlatformUI.getWorkbench().getActiveWorkbenchWindow();
        if (window == null || window.getShell() == null || window.getShell().isDisposed()) {
            return;
        }

        new TextResultDialog(
            window.getShell(),
            command.getTitle(),
            sql,
            previousDiagnostic,
            nextDiagnostic
        ).open();
    }

    private static boolean isSupported(Command command) {
        return command != null && SqldepsCommands.isKnown(command.getCommand());
    }
}
