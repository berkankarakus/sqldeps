using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object Initialize(JsonElement parameters)
    {
        var options = LspInitializationOptions.Parse(parameters);
        clientSupportsPullDiagnostics = options.ClientSupportsPullDiagnostics;
        clientSupportsDiagnosticRefresh = options.ClientSupportsDiagnosticRefresh;
        selectedIndexPath = options.IndexPath;
        allowAutomaticIndexSelection = options.AllowAutomaticIndexSelection;
        experimentalSuggestions = options.ExperimentalSuggestions;
        workspaceRoot = options.WorkspaceRoot;
        workspaceAnalysisRoot = workspaceRoot;
        if (selectedIndexPath is not null)
        {
            var selectedIndexSourceRoot = WorkspaceIndexLocator.SourceRoot(selectedIndexPath);
            if (selectedIndexSourceRoot is not null)
            {
                workspaceAnalysisRoot = selectedIndexSourceRoot;
            }
        }

        if (workspaceAnalysisRoot is not null)
        {
            StartWorkspaceLoad();
        }

        return new
        {
            capabilities = new
            {
                textDocumentSync = new
                {
                    openClose = true,
                    change = 2
                },
                documentSymbolProvider = true,
                workspaceSymbolProvider = true,
                callHierarchyProvider = true,
                definitionProvider = true,
                referencesProvider = true,
                hoverProvider = true,
                completionProvider = new
                {
                    triggerCharacters = new[] { "." }
                },
                signatureHelpProvider = new
                {
                    triggerCharacters = new[] { "(", "," }
                },
                semanticTokensProvider = new
                {
                    legend = new
                    {
                        tokenTypes = new[] { "type", "function" },
                        tokenModifiers = Array.Empty<string>()
                    },
                    full = true,
                },
                codeActionProvider = true,
                documentFormattingProvider = true,
                diagnosticProvider = new
                {
                    identifier = "sqldeps",
                    interFileDependencies = true,
                    workspaceDiagnostics = true
                },
                positionEncoding = "utf-16",
            },
            serverInfo = new
            {
                name = "sqldeps",
                version = CommandLine.ReadVersionText()
            },
        };
    }

    private object? Shutdown()
    {
        shutdownRequested = true;
        workspaceLoadCancellation?.Cancel();

        return null;
    }
}
