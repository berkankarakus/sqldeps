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

import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

import org.eclipse.lsp4j.MessageActionItem;
import org.eclipse.lsp4j.MessageParams;
import org.eclipse.lsp4j.PublishDiagnosticsParams;
import org.eclipse.lsp4j.ShowMessageRequestParams;
import org.eclipse.lsp4j.services.LanguageClient;
import org.sqldeps.dbeaver.TraceSupport;
import org.sqldeps.dbeaver.editor.NestedSqlEditorLspBridge;
import org.sqldeps.dbeaver.editor.SqlEditorLspAddIn;

/** Routes server notifications to the DBeaver diagnostics and log consumers. */
public final class SqldepsLanguageClient implements LanguageClient {
    private final Consumer<PublishDiagnosticsParams> diagnosticsConsumer;
    private final Consumer<String> logConsumer;

    public SqldepsLanguageClient(
        Consumer<PublishDiagnosticsParams> diagnosticsConsumer,
        Consumer<String> logConsumer
    ) {
        this.diagnosticsConsumer = diagnosticsConsumer;
        this.logConsumer = logConsumer;
    }

    @Override
    public void publishDiagnostics(PublishDiagnosticsParams diagnostics) {
        diagnosticsConsumer.accept(diagnostics);
    }

    @Override
    public void showMessage(MessageParams messageParams) {
        logConsumer.accept(messageParams.getMessage());
    }

    @Override
    public CompletableFuture<MessageActionItem> showMessageRequest(ShowMessageRequestParams requestParams) {
        return CompletableFuture.completedFuture(null);
    }

    @Override
    public void logMessage(MessageParams message) {
        logConsumer.accept(message.getMessage());
    }

    @Override
    public void telemetryEvent(Object object) {
        logConsumer.accept(String.valueOf(object));
    }

    @Override
    public CompletableFuture<Void> refreshDiagnostics() {
        if (TraceSupport.enabled()) {
            logConsumer.accept("sqldeps trace: LSP requested a diagnostic refresh");
        }

        SqlEditorLspAddIn.refreshAllDiagnostics();
        NestedSqlEditorLspBridge.refreshAllDiagnostics();

        return CompletableFuture.completedFuture(null);
    }
}

