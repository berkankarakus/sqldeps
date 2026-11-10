using System.Text.Json;
using System.Text.RegularExpressions;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Serves SQL dependency navigation to an LSP client such as Neovim.</summary>
public sealed partial class LanguageServer
{
    private const int MaxDocumentCharacters = 16 * 1024 * 1024;
    private const long MaxDocumentBytes = MaxDocumentCharacters * 4L;
    private const int MaxSymbolResults = 200;
    private const int MaxRepositoryContextNames = 20;

    private readonly JsonRpcConnection connection = new(Console.OpenStandardInput(), Console.OpenStandardOutput());
    private readonly OpenDocumentStore openDocuments = new();
    private readonly Dictionary<string, CachedDiagnostics> diagnosticCache = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly SemaphoreSlim workspaceLoadGate = new(1, 1);
    private AnalysisResult? workspace;
    private string? workspaceRoot;
    private string? workspaceAnalysisRoot;
    private string? selectedIndexPath;
    private bool allowAutomaticIndexSelection = true;
    private bool experimentalSuggestions;
    private int workspaceLoadVersion;
    private CancellationTokenSource? workspaceLoadCancellation;
    private Task? workspaceLoadTask;
    private WorkspaceMetadataSnapshot? workspaceMetadataSnapshot;
    private int workspaceMetadataRevision;
    private bool shutdownRequested;
    private bool clientSupportsPullDiagnostics;
    private bool clientSupportsDiagnosticRefresh;
    private bool refactorQueriesInitialized;
    private IndexQueryService? refactorQueries;

    private sealed record CachedDiagnostics(int DocumentRevision, int WorkspaceRevision, string Source, object[] Items);

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
                textDocumentSync = new { openClose = true, change = 2 },
                documentSymbolProvider = true,
                workspaceSymbolProvider = true,
                callHierarchyProvider = true,
                definitionProvider = true,
                referencesProvider = true,
                hoverProvider = true,
                completionProvider = new { triggerCharacters = new[] { "." } },
                signatureHelpProvider = new { triggerCharacters = new[] { "(", "," } },
                semanticTokensProvider = new
                {
                    legend = new { tokenTypes = new[] { "type", "function" }, tokenModifiers = Array.Empty<string>() },
                    full = true,
                },
                codeActionProvider = true,
                documentFormattingProvider = true,
                diagnosticProvider = new { identifier = "sqldeps", interFileDependencies = true, workspaceDiagnostics = true },
                positionEncoding = "utf-16",
            },
            serverInfo = new { name = "sqldeps", version = CommandLine.ReadVersionText() },
        };
    }

    private object? Shutdown()
    {
        shutdownRequested = true;
        workspaceLoadCancellation?.Cancel();
        return null;
    }

    [GeneratedRegex(@"(?<alias>[\w\[\]]+)\.(?<prefix>[\w\[\]]*)$")]
    private static partial Regex AliasCompletionPattern();

    [GeneratedRegex(@"(?<name>(?:[\w\[\]]+\.)*[\w\[\]]*)$")]
    private static partial Regex ObjectCompletionPattern();

    [GeneratedRegex(@"\b(?:FROM|JOIN)\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,3})(?:\s+(?:AS\s+)?(?<alias>\w+))?", RegexOptions.IgnoreCase)]
    private static partial Regex TableAliasPattern();

    [GeneratedRegex(@"\bSELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex SelectPattern();

    [GeneratedRegex(@"\bFROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"^\s*(?:(?<value>.+?)\s*(?<operator><>|!=|=)\s*NULL|NULL\s*(?<reverseOperator><>|!=|=)\s*(?<reverseValue>.+?))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NullComparisonPattern();

    [GeneratedRegex(@"known as alias '(?<alias>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex AliasInDiagnosticPattern();

    [GeneratedRegex(@"exists in (?<candidates>.+)\. Qualify", RegexOptions.IgnoreCase)]
    private static partial Regex AmbiguousColumnCandidatesPattern();

    [GeneratedRegex(@"Did you mean '(?<suggestion>[^']+)'\?", RegexOptions.IgnoreCase)]
    private static partial Regex SuggestedColumnPattern();

    private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

}
