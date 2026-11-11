using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Storage;
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
    private readonly DiagnosticCache diagnosticCache = new();
    private readonly object gate = new();
    private readonly object pendingRequestGate = new();
    private readonly List<Task> pendingRequests = [];
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

}
