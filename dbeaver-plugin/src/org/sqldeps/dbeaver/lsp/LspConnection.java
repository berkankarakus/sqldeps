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
package org.sqldeps.dbeaver.lsp;

import java.util.List;
import java.util.concurrent.CompletableFuture;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.jsonrpc.messages.Either;

/** Minimal document and code-action contract exposed by an sqldeps LSP process. */
public interface LspConnection extends AutoCloseable {
    /** Opens or replaces a document snapshot in the language server. */
    void didOpen(DocumentSnapshot snapshot);

    /** Sends the latest complete document text to the language server. */
    void didChange(DocumentSnapshot snapshot);

    /** Closes a document without necessarily closing the shared process. */
    void didClose(String uri);

    /** Requests pull diagnostics for a document URI. */
    CompletableFuture<DocumentDiagnosticReport> requestDiagnostics(String uri);

    /** Requests safe code actions for a selected diagnostic range. */
    CompletableFuture<List<Either<Command, CodeAction>>> requestCodeActions(CodeActionParams params);

    /** Executes a command action after the adapter has applied its whitelist. */
    CompletableFuture<Object> executeCommand(Command command);

    @Override
    void close();
}
