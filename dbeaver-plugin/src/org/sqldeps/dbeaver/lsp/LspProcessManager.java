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

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

import org.eclipse.lsp4j.ClientCapabilities;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.CodeActionOptions;
import org.eclipse.lsp4j.CodeActionParams;
import org.eclipse.lsp4j.DiagnosticCapabilities;
import org.eclipse.lsp4j.DidChangeTextDocumentParams;
import org.eclipse.lsp4j.DidCloseTextDocumentParams;
import org.eclipse.lsp4j.DidOpenTextDocumentParams;
import org.eclipse.lsp4j.DocumentDiagnosticParams;
import org.eclipse.lsp4j.DocumentDiagnosticReport;
import org.eclipse.lsp4j.ExecuteCommandParams;
import org.eclipse.lsp4j.InitializeParams;
import org.eclipse.lsp4j.InitializeResult;
import org.eclipse.lsp4j.InitializedParams;
import org.eclipse.lsp4j.TextDocumentClientCapabilities;
import org.eclipse.lsp4j.TextDocumentContentChangeEvent;
import org.eclipse.lsp4j.TextDocumentIdentifier;
import org.eclipse.lsp4j.TextDocumentItem;
import org.eclipse.lsp4j.VersionedTextDocumentIdentifier;
import org.eclipse.lsp4j.launch.LSPLauncher;
import org.eclipse.lsp4j.jsonrpc.Launcher;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.eclipse.lsp4j.services.LanguageServer;

public final class LspProcessManager implements AutoCloseable {
    private static final long INITIALIZE_TIMEOUT_SECONDS = 10;
    private static final long SHUTDOWN_TIMEOUT_SECONDS = 2;

    private final Process process;
    private final Future<?> listeningFuture;
    private final ExecutorService ioExecutor;
    private final boolean codeActionsSupported;
    private final LanguageServer server;
    private final Consumer<String> logConsumer;
    private final Thread stderrThread;
    private final Thread processMonitor;
    private volatile boolean closing;

    private LspProcessManager(
        Process process,
        Future<?> listeningFuture,
        ExecutorService ioExecutor,
        LanguageServer server,
        boolean codeActionsSupported,
        Consumer<String> logConsumer,
        Thread stderrThread
    ) {
        this.process = process;
        this.listeningFuture = listeningFuture;
        this.ioExecutor = ioExecutor;
        this.server = server;
        this.codeActionsSupported = codeActionsSupported;
        this.logConsumer = logConsumer;
        this.stderrThread = stderrThread;
        this.processMonitor = monitorProcess(process, logConsumer);
    }

    public static LspProcessManager start(
        List<String> command,
        String rootUri,
        String indexPath,
        boolean automaticIndexSelection,
        Consumer<org.eclipse.lsp4j.PublishDiagnosticsParams> diagnosticsConsumer,
        Consumer<String> logConsumer
    ) throws Exception {
        Process process = new ProcessBuilder(command).start();
        Thread stderrThread = drainStderr(process, logConsumer);
        ExecutorService ioExecutor = Executors.newCachedThreadPool(
            runnable -> {
                Thread thread = new Thread(runnable, "sqldeps-lsp-jsonrpc");
                thread.setDaemon(true);
                return thread;
            }
        );
        try {
            SqldepsLanguageClient client = new SqldepsLanguageClient(diagnosticsConsumer, logConsumer);
            Launcher<LanguageServer> launcher = createLauncher(client, process, ioExecutor);
            Future<?> listeningFuture = launcher.startListening();

            LanguageServer server = launcher.getRemoteProxy();
            InitializeResult initializeResult = initialize(
                server,
                rootUri,
                indexPath,
                automaticIndexSelection
            );
            if (initializeResult == null || initializeResult.getCapabilities() == null) {
                throw new IOException("sqldeps LSP returned no initialize capabilities");
            }

            return new LspProcessManager(
                process,
                listeningFuture,
                ioExecutor,
                server,
                supportsCodeActions(initializeResult),
                logConsumer,
                stderrThread
            );
        }

        catch (Exception exception) {
            ioExecutor.shutdownNow();
            process.destroyForcibly();
            throw exception;
        }
    }

    private static Launcher<LanguageServer> createLauncher(
        SqldepsLanguageClient client,
        Process process,
        ExecutorService ioExecutor
    ) {
        PrintWriter trace = Boolean.getBoolean("sqldeps.lsp.trace")
            ? new PrintWriter(System.err, true)
            : null;

        return new LSPLauncher.Builder<LanguageServer>()
            .setLocalService(client)
            .setRemoteInterface(LanguageServer.class)
            .setInput(process.getInputStream())
            .setOutput(process.getOutputStream())
            .setExecutorService(ioExecutor)
            .validateMessages(false)
            .traceMessages(trace)
            .create();
    }

    @SuppressWarnings("deprecation")
    private static InitializeResult initialize(
        LanguageServer server,
        String rootUri,
        String indexPath,
        boolean automaticIndexSelection
    ) throws Exception {
        InitializeParams params = new InitializeParams();
        params.setProcessId((int) ProcessHandle.current().pid());
        params.setRootUri(rootUri);

        Map<String, Object> initializationOptions = new HashMap<>();
        initializationOptions.put("automaticIndexSelection", automaticIndexSelection);
        if (indexPath != null) {
            initializationOptions.put("indexPath", indexPath);
        }
        params.setInitializationOptions(initializationOptions);

        ClientCapabilities clientCapabilities = new ClientCapabilities();
        TextDocumentClientCapabilities textDocumentCapabilities = new TextDocumentClientCapabilities();
        textDocumentCapabilities.setDiagnostic(new DiagnosticCapabilities(true));
        clientCapabilities.setTextDocument(textDocumentCapabilities);
        params.setCapabilities(clientCapabilities);

        InitializeResult result = server.initialize(params).get(INITIALIZE_TIMEOUT_SECONDS, TimeUnit.SECONDS);
        server.initialized(new InitializedParams());

        return result;
    }

    private static boolean supportsCodeActions(InitializeResult initializeResult) {
        var provider = initializeResult.getCapabilities().getCodeActionProvider();
        if (provider == null) {
            return false;
        }

        if (provider.isLeft()) {
            return Boolean.TRUE.equals(provider.getLeft());
        }

        return provider.getRight() instanceof CodeActionOptions;
    }

    private static Thread drainStderr(Process process, Consumer<String> logConsumer) {
        Thread thread = new Thread(
            () -> {
                try (BufferedReader reader = new BufferedReader(
                    new InputStreamReader(process.getErrorStream(), StandardCharsets.UTF_8)
                )) {
                    String line;
                    while ((line = reader.readLine()) != null) {
                        logConsumer.accept(line);
                    }
                }

                catch (IOException exception) {
                    logConsumer.accept("sqldeps stderr read failed: " + exception.getMessage());
                }
            },
            "sqldeps-lsp-stderr"
        );
        thread.setDaemon(true);
        thread.start();

        return thread;
    }

    private Thread monitorProcess(Process process, Consumer<String> logConsumer) {
        Thread thread = new Thread(
            () -> {
                try {
                    int exitCode = process.waitFor();
                    if (!closing) {
                        logConsumer.accept("sqldeps LSP süreci beklenmedik şekilde sonlandı: " + exitCode);
                    }
                }

                catch (InterruptedException exception) {
                    Thread.currentThread().interrupt();
                }
            },
            "sqldeps-lsp-process"
        );
        thread.setDaemon(true);
        thread.start();

        return thread;
    }

    public void didOpen(DocumentSnapshot snapshot) {
        TextDocumentItem document = new TextDocumentItem();
        document.setUri(snapshot.uri());
        document.setLanguageId("sql");
        document.setVersion(snapshot.version());
        document.setText(snapshot.text());

        DidOpenTextDocumentParams params = new DidOpenTextDocumentParams();
        params.setTextDocument(document);
        server.getTextDocumentService().didOpen(params);
    }

    public void didChange(DocumentSnapshot snapshot) {
        VersionedTextDocumentIdentifier document = new VersionedTextDocumentIdentifier();
        document.setUri(snapshot.uri());
        document.setVersion(snapshot.version());

        TextDocumentContentChangeEvent change = new TextDocumentContentChangeEvent();
        change.setText(snapshot.text());

        DidChangeTextDocumentParams params = new DidChangeTextDocumentParams();
        params.setTextDocument(document);
        params.setContentChanges(List.of(change));
        server.getTextDocumentService().didChange(params);
    }

    public void didClose(String uri) {
        DidCloseTextDocumentParams params = new DidCloseTextDocumentParams();
        params.setTextDocument(new TextDocumentIdentifier(uri));
        server.getTextDocumentService().didClose(params);
    }

    public CompletableFuture<DocumentDiagnosticReport> requestDiagnostics(String uri) {
        DocumentDiagnosticParams params = new DocumentDiagnosticParams();
        params.setTextDocument(new TextDocumentIdentifier(uri));

        return server.getTextDocumentService().diagnostic(params);
    }

    public CompletableFuture<List<Either<org.eclipse.lsp4j.Command, CodeAction>>> requestCodeActions(
        CodeActionParams params
    ) {
        if (!codeActionsSupported) {
            return CompletableFuture.completedFuture(List.of());
        }

        return server.getTextDocumentService().codeAction(params);
    }

    public CompletableFuture<Object> executeCommand(org.eclipse.lsp4j.Command command) {
        ExecuteCommandParams params = new ExecuteCommandParams();
        params.setCommand(command.getCommand());
        params.setArguments(command.getArguments());

        return server.getWorkspaceService().executeCommand(params);
    }

    @Override
    public void close() {
        closing = true;

        try {
            server.shutdown().get(SHUTDOWN_TIMEOUT_SECONDS, TimeUnit.SECONDS);
        }

        catch (Exception exception) {
            logConsumer.accept("sqldeps LSP shutdown failed: " + exception.getMessage());
        }

        try {
            server.exit();
        }

        catch (RuntimeException exception) {
            logConsumer.accept("sqldeps LSP exit notification failed: " + exception.getMessage());
        }

        listeningFuture.cancel(true);
        ioExecutor.shutdownNow();

        if (process.isAlive()) {
            process.destroy();
        }

        try {
            process.waitFor(SHUTDOWN_TIMEOUT_SECONDS, TimeUnit.SECONDS);
        }

        catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        }

        if (process.isAlive()) {
            process.destroyForcibly();
        }

        if (stderrThread.isAlive()) {
            stderrThread.interrupt();
        }

        if (processMonitor.isAlive()) {
            processMonitor.interrupt();
        }
    }
}

