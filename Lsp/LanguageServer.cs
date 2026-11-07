using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

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
    private bool refactorQueriesInitialized;
    private IndexQueryService? refactorQueries;

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
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: " + exception.Message);
            return 1;
        }
    }

    private bool Handle(JsonElement request)
    {
        if (!TryReadRequest(request, out var method, out var parameters, out var hasId, out var id))
        {
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

    private object ExecuteCommand(JsonElement parameters)
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

    private object Initialize(JsonElement parameters)
    {
        var options = LspInitializationOptions.Parse(parameters);
        clientSupportsPullDiagnostics = options.ClientSupportsPullDiagnostics;
        selectedIndexPath = options.IndexPath;
        allowAutomaticIndexSelection = options.AllowAutomaticIndexSelection;
        experimentalSuggestions = options.ExperimentalSuggestions;
        workspaceRoot = options.WorkspaceRoot;
        workspaceAnalysisRoot = workspaceRoot;
        if (workspaceAnalysisRoot is null && selectedIndexPath is not null)
        {
            workspaceAnalysisRoot = WorkspaceIndexLocator.SourceRoot(selectedIndexPath);
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
            serverInfo = new { name = "sqldeps", version = "0.1" },
        };
    }

    private object? Shutdown()
    {
        shutdownRequested = true;
        workspaceLoadCancellation?.Cancel();
        return null;
    }

    private void Open(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var uri = document.GetProperty("uri").GetString()!;
        var text = document.GetProperty("text").GetString() ?? "";
        Update(uri, text, DocumentVersion(document));
    }

    private void Change(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var uri = document.GetProperty("uri").GetString()!;
        var version = DocumentVersion(document);
        if (IsOlderDocumentVersion(uri, version))
        {
            return;
        }

        var changes = parameters.GetProperty("contentChanges");
        var text = ApplyContentChanges(uri, changes);
        Update(uri, text, version);
    }

    private string ApplyContentChanges(string uri, JsonElement changes)
    {
        var currentDocument = OpenDocument(uri);
        if (currentDocument is null)
        {
            throw new InvalidOperationException("Cannot apply a document change before didOpen.");
        }

        return DocumentChangeApplier.Apply(currentDocument.Text, changes);
    }

    private void Close(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        lock (gate)
        {
            openDocuments.Remove(uri);
            InvalidateWorkspaceMetadataSnapshotLocked();
        }

        RefreshWorkspaceFile(LspUri.PathFromUri(uri));
        PublishDiagnostics(new { uri, diagnostics = Array.Empty<object>() });
    }

    private void Save(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        RefreshWorkspaceFile(LspUri.PathFromUri(uri));
    }

    private void Update(string uri, string text, int? version = null)
    {
        var sourceFile = LspUri.PathFromUri(uri);
        EnsureDocumentPathAllowed(sourceFile);
        if (IsOlderDocumentVersion(uri, version))
        {
            return;
        }

        var analysis = AnalyzeDocument(sourceFile, text, workspaceRoot);
        if (!StoreDocument(uri, analysis, version, allowSameVersion: false))
        {
            return;
        }

        AddLiveDocumentFindings(analysis);

        if (!StoreDocument(uri, analysis, version, allowSameVersion: true))
        {
            return;
        }

        PublishDiagnostics(new
        {
            uri,
            diagnostics = DiagnosticsFor(analysis, "live-document"),
        });
    }

    private void AddLiveDocumentFindings(AnalysisResult analysis)
    {
        MetadataCatalog metadata = new(
            AllResultColumns(),
            AllTableColumns(),
            AllTableKeys().Concat(analysis.TableKeys),
            AllForeignKeys().Concat(analysis.ForeignKeys),
            AllIndexColumns().Concat(analysis.IndexColumns));

        analysis.LintFindings.AddRange(QueryColumnValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, metadata, analysis.LocalColumns, analysis.Text, workspaceRoot));
        analysis.LintFindings.AddRange(ProcedureParameterValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, AllObjects().Select(item => item.Object), AllParameters(), analysis.Text, workspaceRoot));
    }

    private bool StoreDocument(string uri, AnalysisResult analysis, int? version, bool allowSameVersion)
    {
        lock (gate)
        {
            if (!openDocuments.Store(uri, analysis, version, allowSameVersion))
            {
                return false;
            }

            InvalidateWorkspaceMetadataSnapshotLocked();

            return true;
        }
    }

    private void PublishDiagnostics(object diagnostics)
    {
        if (clientSupportsPullDiagnostics)
        {
            return;
        }

        connection.Notify("textDocument/publishDiagnostics", diagnostics);
    }

    private static int? DocumentVersion(JsonElement document)
    {
        return LspDocumentVersion.Read(document);
    }

    private bool IsOlderDocumentVersion(string uri, int? version)
    {
        lock (gate)
        {
            return IsOlderDocumentVersionLocked(uri, version);
        }
    }

    private bool IsOlderDocumentVersionLocked(string uri, int? version)
    {
        return openDocuments.IsStale(uri, version, allowSameVersion: false);
    }

    private object DocumentDiagnostics(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var analysis = OpenDocument(uri);

        return new
        {
            kind = "full",
            items = analysis is null ? Array.Empty<object>() : DiagnosticsFor(analysis, "live-document"),
        };
    }

    private object[] DiagnosticsFor(AnalysisResult analysis, string source)
    {
        var problemMetadata = DiagnosticMetadataFor(source, "parse-error", "error");
        var partialDiagnostics = PartialDiagnosticsFor(analysis, source);
        var problems = analysis.Problems
            .DistinctBy(problem => (problem.Line, problem.Column, problem.Message))
            .Select(problem => (object)new
            {
                range = Range(problem.Line, problem.Column, 1),
                severity = 1,
                source = "sqldeps",
                code = (string?)null,
                data = DiagnosticData(problemMetadata),
                message = problem.Message,
            });
        var findings = DistinctLintFindings(analysis.LintFindings)
            .Select(finding =>
            {
                var metadata = DiagnosticMetadataFor(source, finding.Rule, finding.Severity);
                return (object)new
                {
                    range = Range(finding.Line, finding.Column, finding.Length),
                    severity = DiagnosticSeverity(finding.Severity, metadata.IsStale),
                    source = "sqldeps",
                    code = (string?)finding.Rule,
                    data = DiagnosticData(metadata),
                    relatedInformation = RelatedInformation(finding),
                    message = finding.Message,
                };
            });

        return partialDiagnostics.Concat(problems).Concat(findings).ToArray();
    }

    private static IEnumerable<object> PartialDiagnosticsFor(AnalysisResult analysis, string source)
    {
        if (!analysis.IsPartial)
        {
            return [];
        }

        var metadata = DiagnosticMetadataFor(source, "partial-analysis", "warning");
        var message = analysis.PartialReason ?? "Only part of this SQL document was analyzed.";

        return
        [
            new
            {
                range = new LspRange(new(0, 0), new(0, 1)),
                severity = DiagnosticSeverity("warning"),
                source = "sqldeps",
                code = "partial-analysis",
                data = DiagnosticData(metadata),
                message,
            },
        ];
    }

    private static IEnumerable<LintFinding> DistinctLintFindings(IEnumerable<LintFinding> findings)
    {
        return findings
            .DistinctBy(finding => (finding.Rule, finding.Line, finding.Column, finding.Length, finding.Message));
    }

    private object[] RelatedInformation(LintFinding finding)
    {
        var definition = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, finding.SourceObject));
        if (definition.Object is null)
        {
            return [];
        }

        if (SamePath(finding.SourceFile, definition.Object.SourceFile))
        {
            return [];
        }

        return
        [
            new
            {
                location = Location(definition.Object.SourceFile, definition.Object.Line),
                message = $"Source object '{definition.Object.Name}' is defined here.",
            },
        ];
    }

    private static AnalysisResult AnalyzeDocument(string sourceFile, string text, string? repositoryRoot = null)
    {
        if (text.Length <= MaxDocumentCharacters)
        {
            return DocumentAnalysis.Analyze(sourceFile, text, repositoryRoot: repositoryRoot);
        }

        return DocumentAnalysis.AnalyzeLimited(
            sourceFile,
            text,
            MaxDocumentCharacters,
            repositoryRoot: repositoryRoot);
    }

    private object DocumentSymbols(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        AnalysisResult? analysis;
        lock (gate)
        {
            analysis = openDocuments.Get(uri);
        }

        return analysis?.Objects.Select(Symbol).ToArray() ?? Array.Empty<object>();
    }

    private object WorkspaceSymbols(JsonElement parameters)
    {
        var query = parameters.TryGetProperty("query", out var value) ? value.GetString() ?? "" : "";
        return AllObjects()
            .Where(item => item.Object.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSymbolResults)
            .Select(item => new
            {
                name = item.Object.Name,
                kind = SymbolKind(item.Object.Kind),
                location = Location(item.Object.SourceFile, item.Object.Line),
                containerName = item.Object.Kind,
            })
            .ToArray();
    }

    private object Definitions(JsonElement parameters)
    {
        var name = NameAt(parameters);
        return name is null
            ? Array.Empty<LspLocation>()
            : AllObjects().Where(item => SameName(item.Object.Name, name)).Select(item => Location(item.Object.SourceFile, item.Object.Line)).ToArray();
    }

    private object References(JsonElement parameters)
    {
        var name = NameAt(parameters);
        if (name is null)
        {
            return Array.Empty<LspLocation>();
        }

        var references = AllDependencies()
            .Where(dependency => SameName(dependency.TargetObject, name))
            .Select(dependency => Location(dependency.SourceFile, dependency.Line))
            .ToList();
        var includeDeclaration = parameters.TryGetProperty("context", out var context)
            && context.TryGetProperty("includeDeclaration", out var include)
            && include.GetBoolean();

        if (includeDeclaration)
        {
            references.AddRange(AllObjects().Where(item => SameName(item.Object.Name, name)).Select(item => Location(item.Object.SourceFile, item.Object.Line)));
        }

        return references;
    }

    private object? SuppressLintAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        var indentation = text is null ? "" : LineIndentation(text, diagnostic.Range.Start.Line);
        return QuickFix(
            $"Disable sqldeps '{diagnostic.Rule}' for this line",
            uri,
            new(new(diagnostic.Range.Start.Line, 0), new(diagnostic.Range.Start.Line, 0)),
            indentation + $"-- sqldeps: disable-next-line={diagnostic.Rule}\n",
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected rule is disabled only for the following line; SQL semantics are unchanged.",
            includeRefactorCandidate: false
        );
    }

    private object? SuppressFileAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (LintSuppressions.HasFileSuppression(text, diagnostic.Rule))
        {
            return null;
        }

        return QuickFix(
            $"Disable sqldeps '{diagnostic.Rule}' for this file",
            uri,
            new(new(0, 0), new(0, 0)),
            $"-- sqldeps: disable-file={diagnostic.Rule}\n",
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected rule is disabled for this file; SQL semantics are unchanged.",
            includeRefactorCandidate: false);
    }

    private object? SuppressRepositoryAction(LintDiagnostic diagnostic)
    {
        if (workspaceRoot is null)
        {
            return null;
        }

        var configPath = Path.Combine(workspaceRoot, ".sqldeps");

        if (!File.Exists(configPath))
        {
            return null;
        }

        string configText;

        try
        {
            configText = File.ReadAllText(configPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (LintSuppressions.HasRepositorySuppression(configText, diagnostic.Rule))
        {
            return null;
        }

        var lines = configText.Split('\n');
        var line = lines.Length - 1;
        var character = lines[line].TrimEnd('\r').Length;
        var newText = configText.EndsWith('\n')
            ? $"-- sqldeps: disable-repo={diagnostic.Rule}\n"
            : $"\n-- sqldeps: disable-repo={diagnostic.Rule}\n";
        var configUri = LspUri.FromPath(configPath);
        var range = new LspRange(new(line, character), new(line, character));
        var title = $"Disable sqldeps '{diagnostic.Rule}' for this repository";

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(
                title,
                configUri,
                diagnostic.Confidence,
                [(range, newText)],
                diagnosticRule: diagnostic.Rule,
                applicability: "The selected rule is disabled in the repository .sqldeps configuration; SQL semantics are unchanged.",
                includeRefactorCandidate: false),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [configUri] = new[] { new { range, newText } },
                },
            },
        };
    }

    private static string LineIndentation(string text, int lineNumber)
    {
        var lines = text.Split('\n');
        if (lineNumber < 0 || lineNumber >= lines.Length)
        {
            return "";
        }

        var line = lines[lineNumber].TrimEnd('\r');
        var length = 0;
        while (length < line.Length && char.IsWhiteSpace(line[length]))
        {
            length++;
        }

        return line[..length];
    }

    private object? NullComparisonAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "null-comparison")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var comparison = TextInRange(text, diagnostic.Range);

        if (comparison is null)
        {
            return null;
        }

        if (!TryNullComparisonReplacement(comparison, out var replacement))
        {
            return null;
        }

        return QuickFix(
            $"Replace with {replacement}",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The NULL comparison must be rewritten without changing the intended predicate semantics.");
    }

    private object? AliasReplacementAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "table-name-after-alias")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var reference = TextInRange(text, diagnostic.Range);

        if (reference is null)
        {
            return null;
        }

        if (!TryAliasReplacement(reference, diagnostic.Message, out var replacement, out var alias))
        {
            return null;
        }

        return QuickFix(
            $"Replace table name with alias '{alias}'",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The reference must use the alias declared for the source table in this query scope.");
    }

    private IEnumerable<object> AmbiguousColumnActions(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "ambiguous-column" || text is null)
        {
            return [];
        }

        var candidates = AmbiguousColumnCandidates(diagnostic.Message);

        if (candidates.Count == 0)
        {
            return [];
        }

        var column = TextInRange(text, diagnostic.Range);

        if (column is null)
        {
            return [];
        }

        return candidates
            .Select(candidate => QuickFix(
                $"Qualify with '{candidate}'",
                uri,
                diagnostic.Range,
                $"{candidate}.{column}",
                text,
                diagnostic.Confidence,
                diagnosticRule: diagnostic.Rule,
                applicability: "The selected table alias must uniquely own the ambiguous column."))
            .OfType<object>();
    }

    private static IReadOnlyList<string> AmbiguousColumnCandidates(string message)
    {
        var match = AmbiguousColumnCandidatesPattern().Match(message);

        return match.Success ? match.Groups["candidates"].Value.Split(", ") : [];
    }

    private object? QuickFix(
        string title,
        string uri,
        LspRange range,
        string newText,
        string? text,
        string diagnosticConfidence = "high",
        IReadOnlyList<DiagnosticReference>? references = null,
        bool experimental = false,
        string? diagnosticRule = null,
        string? applicability = null,
        bool includeRefactorCandidate = true
    )
    {
        if (text is null)
        {
            return null;
        }

        if (!IsDocumentRange(text, range))
        {
            return null;
        }

        if (!TryValidateRewrite(text, [(range, newText)], range.Start.Line, out _))
        {
            return null;
        }

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(
                title,
                uri,
                diagnosticConfidence,
                [(range, newText)],
                references,
                experimental,
                diagnosticRule,
                applicability,
                includeRefactorCandidate),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [uri] = new[] { new { range, newText } }
                }
            }
        };
    }

    private static bool TryAliasReplacement(string reference, string message, out string replacement, out string alias)
    {
        var aliasMatch = AliasInDiagnosticPattern().Match(message);
        var separator = reference.LastIndexOf('.');

        if (IsInvalidAliasReplacement(aliasMatch, separator, reference.Length))
        {
            replacement = "";
            alias = "";
            return false;
        }

        alias = aliasMatch.Groups["alias"].Value;
        replacement = alias + reference[separator..];

        return true;
    }

    private static bool IsInvalidAliasReplacement(Match aliasMatch, int separator, int referenceLength)
    {
        return !aliasMatch.Success
            || separator <= 0
            || separator == referenceLength - 1;
    }

    private static string? TextInRange(string text, LspRange range)
    {
        if (range.Start.Line != range.End.Line || range.Start.Line < 0)
        {
            return null;
        }

        var lines = text.Split('\n');

        if (range.Start.Line >= lines.Length)
        {
            return null;
        }

        var line = lines[range.Start.Line].TrimEnd('\r');
        var start = range.Start.Character;
        var length = range.End.Character - start;

        return start < 0 || length < 0 || start + length > line.Length ? null : line.Substring(start, length);
    }

    private static bool TryNullComparisonReplacement(string comparison, out string replacement)
    {
        var match = NullComparisonPattern().Match(comparison);

        if (!match.Success)
        {
            replacement = "";
            return false;
        }

        var operatorText = match.Groups["operator"].Success
            ? match.Groups["operator"].Value
            : match.Groups["reverseOperator"].Value;

        var value = match.Groups["value"].Success
            ? match.Groups["value"].Value
            : match.Groups["reverseValue"].Value;

        replacement = $"{value.Trim()} IS{(operatorText == "=" ? "" : " NOT")} NULL";

        return true;
    }

    private IEnumerable<(DatabaseObject Object, string Uri)> AllObjects()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Objects;
        }
    }

    private static bool IsTableOrView([NotNullWhen(true)] DatabaseObject? databaseObject)
    {
        if (databaseObject is null)
        {
            return false;
        }

        return databaseObject.Kind is "table" or "view";
    }

    private WorkspaceMetadataSnapshot MetadataSnapshotLocked()
    {
        if (workspaceMetadataSnapshot is not null
            && workspaceMetadataSnapshot.Revision == workspaceMetadataRevision)
        {
            return workspaceMetadataSnapshot;
        }

        var openPaths = openDocuments.Values
            .Select(document => document.SourceFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var savedObjects = workspace?.Objects.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedDependencies = workspace?.Dependencies.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedParameters = workspace?.Parameters.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedTableColumns = workspace?.TableColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedResultColumns = workspace?.ResultColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedTableKeys = workspace?.TableKeys.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedIndexColumns = workspace?.IndexColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedForeignKeys = workspace?.ForeignKeys.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];

        workspaceMetadataSnapshot = new WorkspaceMetadataSnapshot(
            workspaceMetadataRevision,
            savedObjects.Concat(openDocuments.Values.SelectMany(document => document.Objects))
                .Select(item => (item, LspUri.FromPath(item.SourceFile)))
                .ToArray(),
            savedDependencies.Concat(openDocuments.Values.SelectMany(document => document.Dependencies)).ToArray(),
            savedParameters.Concat(openDocuments.Values.SelectMany(document => document.Parameters)).ToArray(),
            savedTableColumns.Concat(openDocuments.Values.SelectMany(document => document.TableColumns)).ToArray(),
            savedResultColumns.Concat(openDocuments.Values.SelectMany(document => document.ResultColumns)).ToArray(),
            savedTableKeys.Concat(openDocuments.Values.SelectMany(document => document.TableKeys)).ToArray(),
            savedIndexColumns.Concat(openDocuments.Values.SelectMany(document => document.IndexColumns)).ToArray(),
            savedForeignKeys.Concat(openDocuments.Values.SelectMany(document => document.ForeignKeys)).ToArray());

        return workspaceMetadataSnapshot;
    }

    private void InvalidateWorkspaceMetadataSnapshotLocked()
    {
        workspaceMetadataRevision++;
        workspaceMetadataSnapshot = null;
    }

    private IEnumerable<Dependency> AllDependencies()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Dependencies;
        }
    }

    private IEnumerable<ObjectParameter> AllParameters()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Parameters;
        }
    }

    private IEnumerable<TableColumn> AllTableColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().TableColumns;
        }
    }

    private IEnumerable<ResultColumn> AllResultColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().ResultColumns;
        }
    }

    private IEnumerable<TableKey> AllTableKeys()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().TableKeys;
        }
    }

    private IEnumerable<TableIndexColumn> AllIndexColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().IndexColumns;
        }
    }

    private IEnumerable<TableForeignKey> AllForeignKeys()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().ForeignKeys;
        }
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

        var lines = document.Text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var source = lines[line].TrimEnd('\r');
        var index = Math.Min(Math.Max(character, 0), source.Length);
        var start = index;
        var end = index;
        while (start > 0 && IsNameCharacter(source[start - 1])) start--;
        while (end < source.Length && IsNameCharacter(source[end])) end++;
        var name = source[start..end].Replace("[", "").Replace("]", "");
        return name.Length == 0 ? null : Qualify(name);
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

    private static object DiagnosticData(DiagnosticMetadata metadata) => new
    {
        source = metadata.Source,
        confidence = metadata.Confidence,
        stale = metadata.IsStale,
        confidenceReason = metadata.ConfidenceReason,
    };

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
