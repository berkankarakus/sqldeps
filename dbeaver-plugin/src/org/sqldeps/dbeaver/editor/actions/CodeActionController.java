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

import java.util.ArrayList;
import java.util.Map;
import java.util.List;
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Supplier;

import org.eclipse.jface.text.BadLocationException;
import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionContext;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.swt.widgets.Display;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.sqldeps.dbeaver.TraceSupport;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;

/** Requests, filters, previews, and applies safe active-document code actions. */
public final class CodeActionController {
    private final SQLEditorBase editor;
    private final IDocument document;
    private final DocumentSynchronizer synchronizer;
    private final ScheduledExecutorService scheduler;
    private final Supplier<List<Diagnostic>> diagnosticsSupplier;
    private final BooleanSupplier activeSupplier;
    private final Consumer<String> logConsumer;
    private final CodeActionRangeResolver rangeResolver;
    private final WorkspaceEditApplier workspaceEditApplier;
    private final CodeActionCommandExecutor commandExecutor;
    private final CodeActionUiCoordinator uiCoordinator;

    /** Creates a controller bound to one editor document and its LSP session. */
    public CodeActionController(
        SQLEditorBase editor,
        IDocument document,
        DocumentSynchronizer synchronizer,
        ScheduledExecutorService scheduler,
        Supplier<List<Diagnostic>> diagnosticsSupplier,
        BooleanSupplier activeSupplier,
        Runnable previousDiagnostic,
        Runnable nextDiagnostic,
        Runnable refreshDiagnostics,
        Consumer<String> logConsumer
    ) {
        this.editor = editor;
        this.document = document;
        this.synchronizer = synchronizer;
        this.scheduler = scheduler;
        this.diagnosticsSupplier = diagnosticsSupplier;
        this.activeSupplier = activeSupplier;
        this.logConsumer = logConsumer;
        rangeResolver = new CodeActionRangeResolver(document, diagnosticsSupplier);
        this.workspaceEditApplier = new WorkspaceEditApplier(
            editor,
            document,
            synchronizer,
            refreshDiagnostics,
            logConsumer,
            this::trace);
        CodeActionDialogPresenter dialogPresenter = new CodeActionDialogPresenter(
            previousDiagnostic,
            nextDiagnostic);
        commandExecutor = new CodeActionCommandExecutor(
            synchronizer,
            activeSupplier,
            workspaceEditApplier,
            previousDiagnostic,
            nextDiagnostic,
            logConsumer);
        uiCoordinator = new CodeActionUiCoordinator(
            editor,
            document,
            synchronizer,
            activeSupplier,
            logConsumer,
            this::trace,
            commandExecutor,
            dialogPresenter);
    }

    /** Requests actions for the current selection and displays safe choices. */
    public void show(LspConnection processManager) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: code actions are not ready yet");
            trace("code actions requested before the LSP session was ready");
            return;
        }

        try {
            Range range = rangeResolver.selectedRange(
                editor.getSite().getSelectionProvider().getSelection());
            show(processManager, range, diagnosticsSupplier.get());
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action range could not be read: " + exception.getMessage());
        }
    }

    /** Requests actions for every finding overlapping the current selection. */
    public void showAll(LspConnection processManager) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: code actions are not ready yet");
            trace("all code actions requested before the LSP session was ready");

            return;
        }

        try {
            Range range = rangeResolver.selectedRange(
                editor.getSite().getSelectionProvider().getSelection());
            show(processManager, range, rangeResolver.allDiagnosticsFor(range));
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps all code action range could not be read: " + exception.getMessage());
        }
    }

    /** Runs the impact analysis for the object name under the selection or cursor. */
    public void showImpact(LspConnection processManager) {
        requestSelectionActions(processManager, "impact analysis", (snapshot, actions) -> {
            CodeActionChoice impactChoice = findCommandChoice(actions, "sqldeps.impact");

            if (impactChoice == null) {
                uiCoordinator.reportStatus("sqldeps: place the cursor on an indexed table, view or procedure name");

                return;
            }

            commandExecutor.execute(snapshot, processManager, impactChoice.command());
        });
    }

    /** Formats the whole script through the language server; scripts that do not parse are left untouched. */
    public void formatScript(LspConnection processManager) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: formatting is not ready yet");

            return;
        }

        DocumentSnapshot snapshot = synchronizer.snapshot();

        scheduler.execute(() -> processManager.requestFormatting(snapshot.uri()).whenComplete((edits, exception) -> {
            if (exception != null) {
                uiCoordinator.reportFailure("sqldeps Format", "formatting", exception);

                return;
            }

            if (edits == null || edits.isEmpty()) {
                uiCoordinator.reportStatus("sqldeps: the script is already formatted");

                return;
            }

            WorkspaceEdit edit = new WorkspaceEdit();
            edit.setChanges(Map.of(snapshot.uri(), new ArrayList<TextEdit>(edits)));

            Display display = Display.getDefault();

            if (display.isDisposed()) {
                return;
            }

            display.asyncExec(() -> workspaceEditApplier.applyFormatting(snapshot, edit));
        }));
    }

    private void requestSelectionActions(
        LspConnection processManager,
        String purpose,
        BiConsumer<DocumentSnapshot, List<Either<Command, CodeAction>>> handler
    ) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: " + purpose + " is not ready yet");

            return;
        }

        Range range;

        try {
            range = rangeResolver.selectedRange(editor.getSite().getSelectionProvider().getSelection());
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps " + purpose + " range could not be read: " + exception.getMessage());

            return;
        }

        DocumentSnapshot snapshot = synchronizer.snapshot();
        CodeActionParams params = new CodeActionParams();
        params.setTextDocument(new TextDocumentIdentifier(snapshot.uri()));
        params.setRange(range);
        params.setContext(new CodeActionContext(List.of()));
        trace("requesting " + purpose);

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                uiCoordinator.reportFailure("sqldeps", purpose, exception);

                return;
            }

            handler.accept(snapshot, actions == null ? List.of() : actions);
        }));
    }

    private CodeActionChoice findCommandChoice(List<Either<Command, CodeAction>> actions, String commandName) {
        for (CodeActionChoice choice : buildChoices(actions, List.of())) {
            Command command = choice.command();

            if (command != null && commandName.equals(command.getCommand())) {
                return choice;
            }
        }

        return null;
    }

    /** Requests actions for a diagnostic range supplied by DBeaver's quick-fix UI. */
    public boolean showAt(LspConnection processManager, int offset, int length) {
        if (!isReady(processManager)) {
            setStatus("sqldeps: code actions are not ready yet");
            trace("diagnostic quick fix requested before the LSP session was ready");

            return false;
        }

        try {
            Range range = rangeResolver.rangeAt(offset, length);
            trace("diagnostic quick fix requested at offset " + offset + ", length " + length);
            show(processManager, range, rangeResolver.diagnosticsFor(range));

            return true;
        }

        catch (BadLocationException exception) {
            logConsumer.accept("sqldeps code action range could not be read: " + exception.getMessage());

            return false;
        }
    }

    private void show(
        LspConnection processManager,
        Range range,
        List<Diagnostic> diagnostics
    ) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        CodeActionParams params = new CodeActionParams();
        params.setTextDocument(new TextDocumentIdentifier(snapshot.uri()));
        params.setRange(range);
        params.setContext(new CodeActionContext(List.copyOf(diagnostics)));
        trace("requesting code actions for " + diagnostics.size() + " diagnostic(s)");

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                logConsumer.accept("sqldeps code action request failed: " + exception.getMessage());
                return;
            }

            trace("received " + (actions == null ? 0 : actions.size()) + " raw code action(s)");
            try {
                uiCoordinator.show(snapshot, processManager, actions, diagnostics);
            }

            catch (RuntimeException runtimeException) {
                logConsumer.accept("sqldeps code action UI failed: " + runtimeException.getMessage());
                trace("code action UI failed: " + runtimeException.getClass().getSimpleName());
            }
        }));
    }

    private List<CodeActionChoice> buildChoices(
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        return new CodeActionChoiceBuilder(document).build(actions, diagnostics);
    }

    private boolean isReady(LspConnection processManager) {
        return activeSupplier.getAsBoolean() && processManager != null;
    }

    private void setStatus(String message) {
        editor.getEditorSite().getActionBars().getStatusLineManager().setMessage(message);
    }

    private void trace(String message) {
        TraceSupport.log(logConsumer, message);
    }

}

