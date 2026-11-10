using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const int JsonRpcInvalidRequestCode = -32600;
    private const int JsonRpcInvalidParametersCode = -32602;
    private const int JsonRpcRequestFailedCode = -32803;
    private const int UnixBrokenPipeErrorCode = 32;
    private const int LspCompletionItemKindField = 5;
    private const int LspSymbolKindClass = 5;
    private const int LspSymbolKindFunction = 12;
    private const int LspSymbolKindObject = 19;
    private const int LspSymbolKindStruct = 23;
    private const int LspDiagnosticSeverityError = 1;
    private const int LspDiagnosticSeverityWarning = 2;
    private const int LspDiagnosticSeverityInformation = 3;

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

    private bool IsWorkspaceSqlFile(string path)
    {
        if (workspaceRoot is null)
        {
            return false;
        }

        if (!path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsPathInsideWorkspace(path);
    }

    private bool IsPathInsideWorkspace(string path)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot!, Path.GetFullPath(path));

        return relativePath != ".."
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar)
            && !Path.IsPathRooted(relativePath);
    }

    private void EnsureDocumentPathAllowed(string path)
    {
        if (workspaceRoot is not null && !IsPathInsideWorkspace(path))
        {
            throw new ArgumentException("LSP document is outside the workspace root.");
        }
    }

    private string? NameAt(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var position = parameters.GetProperty("position");
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        AnalysisResult? document;
        lock (gate)
        {
            document = openDocuments.Get(uri);
        }

        if (document is null)
        {
            return null;
        }

        var identifier = IdentifierAt(document.Text, line, character);

        return identifier is null ? null : Qualify(identifier);
    }

    private (AnalysisResult? Document, int Line, int Character) DocumentAt(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var position = parameters.GetProperty("position");
        lock (gate)
        {
            var document = openDocuments.Get(uri);
            return (document, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        }
    }

    private AnalysisResult? OpenDocument(string uri)
    {
        lock (gate)
        {
            return openDocuments.Get(uri);
        }
    }

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '[' or ']';

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

    private static string Qualify(string name) => name.Contains('.') ? name : ObjectNames.StandardDefaultSchema + "." + name;

    private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static object Symbol(DatabaseObject item) => new
    {
        name = item.Name,
        detail = item.Kind,
        kind = SymbolKind(item.Kind),
        range = Range(item.Line),
        selectionRange = Range(item.Line),
    };

    private static int SymbolKind(string kind) => kind switch
    {
        "table" => LspSymbolKindStruct,
        "view" => LspSymbolKindClass,
        "procedure" or "function" or "trigger" => LspSymbolKindFunction,
        _ => LspSymbolKindObject,
    };

    private static int DiagnosticSeverity(string severity) => severity switch
    {
        "error" => LspDiagnosticSeverityError,
        "warning" => LspDiagnosticSeverityWarning,
        _ => LspDiagnosticSeverityInformation,
    };

    private static int DiagnosticSeverity(string severity, bool stale) => stale ? LspDiagnosticSeverityInformation : DiagnosticSeverity(severity);

    private static DiagnosticMetadata DiagnosticMetadataFor(string source, string? rule = null, string? severity = null)
        => DiagnosticMetadataMapper.For(source, rule, severity);

    private static object DiagnosticData(DiagnosticMetadata metadata, string? rule = null) => new
    {
        source = metadata.Source,
        confidence = metadata.Confidence,
        stale = metadata.IsStale,
        confidenceReason = metadata.ConfidenceReason,
        evidence = DiagnosticEvidence(rule),
    };

    private static object? DiagnosticEvidence(string? rule)
    {
        if (rule is null || !DiagnosticRuleCatalog.TryGet(rule, out var explanation))
        {
            return null;
        }

        return new
        {
            rule = explanation.Rule,
            summary = explanation.Summary,
            guidance = explanation.Guidance,
            references = explanation.References ?? [],
        };
    }

    private static LspLocation Location(string path, int line) => new(LspUri.FromPath(path), Range(line));

    private static LspRange Range(int oneBasedLine) => new(new(Math.Max(0, oneBasedLine - 1), 0), new(Math.Max(0, oneBasedLine - 1), 1));

    private static LspRange Range(int oneBasedLine, int oneBasedColumn, int length)
    {
        var line = Math.Max(0, oneBasedLine - 1);
        var start = Math.Max(0, oneBasedColumn - 1);
        return new(new(line, start), new(line, start + Math.Max(1, length)));
    }

    private sealed record LspPosition(
        [property: JsonPropertyName("line")] int Line,
        [property: JsonPropertyName("character")] int Character
    );

    private sealed record LspRange(
        [property: JsonPropertyName("start")] LspPosition Start,
        [property: JsonPropertyName("end")] LspPosition End
    );

    private sealed record LspLocation(
        [property: JsonPropertyName("uri")] string Uri,
        [property: JsonPropertyName("range")] LspRange Range
    );

    private sealed record LintDiagnostic(string Rule, LspRange Range, string Message, string Source = "", string Confidence = "high", bool IsStale = false);
    private sealed record WorkspaceDiagnosticGroup(string Uri, object[] Diagnostics);
}
