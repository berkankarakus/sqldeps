using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    /// <summary>Runs the JSON-RPC LSP server over standard input and output.</summary>
    public static int Run() => new LanguageServer().Serve();

    private int Serve()
    {
        try
        {
            while (true)
            {
                JsonDocument? message;
                try
                {
                    message = connection.Read();
                }
                catch (JsonException exception)
                {
                    Console.Error.WriteLine("sqldeps-lsp: invalid JSON message: " + exception.Message);
                    continue;
                }

                if (message is null)
                {
                    return 0;
                }

                using (message)
                {
                    if (Handle(message.RootElement))
                    {
                        return shutdownRequested ? 0 : 1;
                    }
                }
            }
        }
        catch (IOException exception) when (IsClientDisconnect(exception))
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: " + exception.Message);
            return 1;
        }
    }

    internal static bool IsClientDisconnect(IOException exception)
    {
        return exception.InnerException is SocketException socket
            && (socket.ErrorCode == UnixBrokenPipeErrorCode
                || socket.SocketErrorCode == SocketError.ConnectionReset);
    }

    private bool Handle(JsonElement request)
    {
        if (!TryReadRequest(request, out var method, out var parameters, out var hasId, out var id))
        {
            if (IsResponse(request))
            {
                return false;
            }

            RespondInvalidRequest(request);

            return false;
        }

        if (!TryHandleRequest(method, parameters, hasId, id, out var result))
        {
            return false;
        }

        if (HandleNotificationSafely(method, parameters, hasId, id, out var notificationFailed))
        {
            return true;
        }

        if (hasId && !notificationFailed)
        {
            connection.Respond(id, result);
        }

        return false;
    }

    private static bool IsResponse(JsonElement request)
    {
        return request.ValueKind == JsonValueKind.Object
            && request.TryGetProperty("id", out _)
            && (request.TryGetProperty("result", out _)
                || request.TryGetProperty("error", out _));
    }

    private bool TryHandleRequest(
        string? method,
        JsonElement parameters,
        bool hasId,
        JsonElement id,
        out object? result)
    {
        try
        {
            result = HandleRequest(method, parameters);

            return true;
        }
        catch (LspRequestFailedException exception)
        {
            result = null;
            Console.Error.WriteLine($"sqldeps-lsp: request '{method}' failed: {exception.Message}");

            if (hasId)
            {
                connection.RespondError(id, JsonRpcRequestFailedCode, exception.Message);
            }

            return false;
        }

        catch (Exception exception)
        {
            result = null;
            Console.Error.WriteLine($"sqldeps-lsp: request '{method}' failed: {exception.Message}");

            if (hasId)
            {
                connection.RespondError(id, JsonRpcInvalidParametersCode, "Invalid request parameters.");
            }

            return false;
        }
    }

    private bool HandleNotificationSafely(
        string? method,
        JsonElement parameters,
        bool hasId,
        JsonElement id,
        out bool failed)
    {
        failed = false;

        try
        {
            return HandleNotification(method, parameters);
        }
        catch (Exception exception)
        {
            failed = true;
            Console.Error.WriteLine($"sqldeps-lsp: notification '{method}' failed: {exception.Message}");

            if (hasId)
            {
                connection.RespondError(id, JsonRpcInvalidParametersCode, "Invalid request parameters.");
            }

            return false;
        }
    }

    private static bool TryReadRequest(
        JsonElement request,
        out string? method,
        out JsonElement parameters,
        out bool hasId,
        out JsonElement id)
    {
        return JsonRpcRequestParser.TryRead(
            request,
            out method,
            out parameters,
            out hasId,
            out id);
    }

    private void RespondInvalidRequest(JsonElement request)
    {
        if (request.ValueKind == JsonValueKind.Object && request.TryGetProperty("id", out var invalidId))
        {
            connection.RespondError(invalidId, JsonRpcInvalidRequestCode, "Invalid Request.");
        }
    }

    private object? HandleRequest(string? method, JsonElement parameters) => method switch
    {
        "initialize" => Initialize(parameters),
        "shutdown" => Shutdown(),
        "textDocument/documentSymbol" => DocumentSymbols(parameters),
        "workspace/symbol" => WorkspaceSymbols(parameters),
        "textDocument/prepareCallHierarchy" => PrepareCallHierarchy(parameters),
        "callHierarchy/incomingCalls" => IncomingCalls(parameters),
        "callHierarchy/outgoingCalls" => OutgoingCalls(parameters),
        "textDocument/definition" => Definitions(parameters),
        "textDocument/references" => References(parameters),
        "textDocument/hover" => Hover(parameters),
        "textDocument/completion" => Completion(parameters),
        "textDocument/signatureHelp" => SignatureHelp(parameters),
        "textDocument/semanticTokens/full" => SemanticTokens(parameters),
        "textDocument/codeAction" => CodeActions(parameters),
        "textDocument/formatting" => Formatting(parameters),
        "textDocument/diagnostic" => DocumentDiagnostics(parameters),
        "workspace/diagnostic" => WorkspaceDiagnostics(),
        "workspace/executeCommand" => ExecuteCommand(parameters),
        _ => null,
    };

    private object? ExecuteCommand(JsonElement parameters)
    {
        var command = parameters.GetProperty("command").GetString();

        if (string.Equals(command, "sqldeps.query-builder", StringComparison.Ordinal))
        {
            return ExecuteQueryBuilderCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.index-suggestion", StringComparison.Ordinal))
        {
            return ExecuteIndexSuggestionCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.explain-diagnostic", StringComparison.Ordinal))
        {
            return ExecuteDiagnosticExplanationCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.repository-context", StringComparison.Ordinal))
        {
            return ExecuteRepositoryContextCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.who-writes", StringComparison.Ordinal))
        {
            return ExecuteWhoWritesCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.who-reads", StringComparison.Ordinal))
        {
            return ExecuteWhoReadsCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.impact", StringComparison.Ordinal))
        {
            return ExecuteImpactCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.column-writers", StringComparison.Ordinal))
        {
            return ExecuteColumnWritersCommand(parameters);
        }

        if (string.Equals(command, "sqldeps.repository-root", StringComparison.Ordinal))
        {
            var repositoryRoot = workspaceAnalysisRoot;

            if (repositoryRoot is null)
            {
                repositoryRoot = workspaceRoot;
            }

            return repositoryRoot;
        }

        if (!string.Equals(command, "sqldeps.reindex", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown command: {command}");
        }

        var root = workspaceRoot ?? throw new InvalidOperationException("Workspace root is not initialized.");
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "sqldeps",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        processStartInfo.ArgumentList.Add("reindex");
        processStartInfo.ArgumentList.Add(root);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Could not start sqldeps reindex.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sqldeps reindex failed with exit code {process.ExitCode}.");
        }

        StartWorkspaceLoad();

        return new { exitCode = process.ExitCode };
    }

    private bool HandleNotification(string? method, JsonElement parameters)
    {
        switch (method)
        {
            case "textDocument/didOpen":
                Open(parameters);
                break;
            case "textDocument/didChange":
                Change(parameters);
                break;
            case "textDocument/didSave":
                Save(parameters);
                break;
            case "textDocument/didClose":
                Close(parameters);
                break;
            case "exit":
                return true;
        }

        return false;
    }
}
