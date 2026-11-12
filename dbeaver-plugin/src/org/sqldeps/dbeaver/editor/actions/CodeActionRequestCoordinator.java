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
import java.util.concurrent.ScheduledExecutorService;
import java.util.function.BiConsumer;
import java.util.function.Consumer;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionContext;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.sqldeps.dbeaver.editor.DocumentSynchronizer;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;
import org.sqldeps.dbeaver.lsp.LspConnection;

/** Creates and schedules code-action requests for one synchronized document. */
final class CodeActionRequestCoordinator {
    private final DocumentSynchronizer synchronizer;
    private final ScheduledExecutorService scheduler;
    private final Consumer<String> traceConsumer;

    CodeActionRequestCoordinator(
        DocumentSynchronizer synchronizer,
        ScheduledExecutorService scheduler,
        Consumer<String> traceConsumer
    ) {
        this.synchronizer = synchronizer;
        this.scheduler = scheduler;
        this.traceConsumer = traceConsumer;
    }

    void request(
        LspConnection processManager,
        String purpose,
        org.eclipse.lsp4j.Range range,
        List<Diagnostic> diagnostics,
        BiConsumer<DocumentSnapshot, List<Either<Command, CodeAction>>> success,
        Consumer<Throwable> failure
    ) {
        DocumentSnapshot snapshot = synchronizer.snapshot();
        CodeActionParams params = new CodeActionParams();
        params.setTextDocument(new TextDocumentIdentifier(snapshot.uri()));
        params.setRange(range);
        params.setContext(new CodeActionContext(List.copyOf(diagnostics)));
        traceConsumer.accept("requesting " + purpose);

        scheduler.execute(() -> processManager.requestCodeActions(params).whenComplete((actions, exception) -> {
            if (exception != null) {
                failure.accept(exception);

                return;
            }

            success.accept(snapshot, actions == null ? List.of() : actions);
        }));
    }
}
