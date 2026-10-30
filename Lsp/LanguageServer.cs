using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Serves SQL dependency navigation to an LSP client such as Neovim.</summary>
public sealed partial class LanguageServer
{
    private const int MaxDocumentCharacters = 16 * 1024 * 1024;

    private readonly JsonRpcConnection connection = new(Console.OpenStandardInput(), Console.OpenStandardOutput());
    private readonly Dictionary<string, AnalysisResult> openDocuments = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private AnalysisResult? workspace;
    private string? workspaceRoot;
    private int workspaceLoadVersion;
    private bool shutdownRequested;

    public static int Run() => new LanguageServer().Serve();

    private int Serve()
    {
        try
        {
            while (connection.Read() is { } message)
            {
                using (message)
                {
                    if (Handle(message.RootElement))
                    {
                        return shutdownRequested ? 0 : 1;
                    }
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: " + exception.Message);
            return 1;
        }
    }

    private bool Handle(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement))
        {
            return false;
        }

        var method = methodElement.GetString();
        var parameters = request.TryGetProperty("params", out var value) ? value : default;
        var hasId = request.TryGetProperty("id", out var id);
        object? result;

        try
        {
            result = HandleRequest(method, parameters);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"sqldeps-lsp: request '{method}' failed: {exception.Message}");

            if (hasId)
            {
                connection.RespondError(id, -32602, "Invalid request parameters.");
            }

            return false;
        }

        try
        {
            if (HandleNotification(method, parameters))
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"sqldeps-lsp: notification '{method}' failed: {exception.Message}");
        }

        if (hasId)
        {
            connection.Respond(id, result);
        }

        return false;
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
            "textDocument/codeAction" => CodeActions(parameters),
            "textDocument/diagnostic" => DocumentDiagnostics(parameters),
            "workspace/diagnostic" => WorkspaceDiagnostics(),
            _ => null,
        };

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
        workspaceRoot = WorkspaceRoot(parameters);
        if (workspaceRoot is not null)
        {
            var loadVersion = Interlocked.Increment(ref workspaceLoadVersion);
            _ = Task.Run(() => LoadWorkspace(loadVersion));
        }

        return new
        {
            capabilities = new
            {
                textDocumentSync = 1,
                documentSymbolProvider = true,
                workspaceSymbolProvider = true,
                definitionProvider = true,
                referencesProvider = true,
                hoverProvider = true,
                completionProvider = new { triggerCharacters = new[] { "." } },
                codeActionProvider = true,
                diagnosticProvider = new { identifier = "sqldeps", interFileDependencies = true, workspaceDiagnostics = true },
                positionEncoding = "utf-16",
            },
            serverInfo = new { name = "sqldeps", version = "0.1" },
        };
    }

    private object? Shutdown()
    {
        shutdownRequested = true;
        return null;
    }

    private void Open(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var uri = document.GetProperty("uri").GetString()!;
        var text = document.GetProperty("text").GetString() ?? "";
        Update(uri, text);
    }

    private void Change(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var changes = parameters.GetProperty("contentChanges");
        var text = changes.GetArrayLength() == 0 ? "" : changes[changes.GetArrayLength() - 1].GetProperty("text").GetString() ?? "";
        Update(uri, text);
    }

    private void Close(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        lock (gate)
        {
            openDocuments.Remove(uri);
        }

        RefreshWorkspaceFile(PathFromUri(uri));
        connection.Notify("textDocument/publishDiagnostics", new { uri, diagnostics = Array.Empty<object>() });
    }

    private void Save(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        RefreshWorkspaceFile(PathFromUri(uri));
    }

    private void Update(string uri, string text)
    {
        var sourceFile = PathFromUri(uri);
        var analysis = AnalyzeDocument(sourceFile, text);
        lock (gate)
        {
            openDocuments[uri] = analysis;
        }

        analysis.LintFindings.AddRange(QueryColumnValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, AllTableColumns(), AllResultColumns(), AllTableKeys().Concat(analysis.TableKeys), AllIndexColumns().Concat(analysis.IndexColumns), analysis.LocalColumns));
        analysis.LintFindings.AddRange(ProcedureParameterValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, AllObjects().Select(item => item.Object), AllParameters()));

        connection.Notify("textDocument/publishDiagnostics", new
        {
            uri,
            diagnostics = DiagnosticsFor(analysis, "live-document"),
        });
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

    private static object[] DiagnosticsFor(AnalysisResult analysis, string source)
    {
        var problems = analysis.Problems
            .DistinctBy(problem => (problem.Line, problem.Column, problem.Message))
            .Select(problem => (object)new
            {
                range = Range(problem.Line, problem.Column, 1),
                severity = 1,
                source = "sqldeps",
                code = (string?)null,
                data = new { source, confidence = "high" },
                message = problem.Message,
            });
        var findings = analysis.LintFindings
            .DistinctBy(finding => (finding.Rule, finding.Line, finding.Column, finding.Message))
            .Select(finding => (object)new
            {
                range = Range(finding.Line, finding.Column, finding.Length),
                severity = DiagnosticSeverity(finding.Severity),
                source = "sqldeps",
                code = (string?)finding.Rule,
                data = new { source, confidence = DiagnosticConfidence(finding.Rule) },
                message = finding.Message,
            });

        return problems.Concat(findings).ToArray();
    }

    private static AnalysisResult AnalyzeDocument(string sourceFile, string text)
    {
        if (text.Length <= MaxDocumentCharacters)
        {
            return DocumentAnalysis.Analyze(sourceFile, text);
        }

        AnalysisResult result = new(sourceFile, text);
        result.Problems.Add(new ParseProblem(
            sourceFile,
            1,
            $"SQL document exceeds the {MaxDocumentCharacters / (1024 * 1024)} MiB limit."
        ));

        return result;
    }

    private object DocumentSymbols(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        AnalysisResult? analysis;
        lock (gate)
        {
            openDocuments.TryGetValue(uri, out analysis);
        }

        return analysis?.Objects.Select(Symbol).ToArray() ?? Array.Empty<object>();
    }

    private object WorkspaceSymbols(JsonElement parameters)
    {
        var query = parameters.TryGetProperty("query", out var value) ? value.GetString() ?? "" : "";
        return AllObjects()
            .Where(item => item.Object.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(200)
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

    private object? Hover(JsonElement parameters)
    {
        var name = NameAt(parameters);
        if (name is null)
        {
            return null;
        }

        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, name));
        if (match.Object is null)
        {
            return null;
        }

        var parametersForObject = AllParameters().Where(parameter => SameName(parameter.ObjectName, match.Object.Name)).OrderBy(parameter => parameter.Ordinal);
        var signature = string.Join(", ", parametersForObject.Select(parameter => parameter.Name + " " + parameter.DataType + (parameter.IsOutput ? " OUTPUT" : "")));
        var value = $"**{match.Object.Kind}** `{match.Object.Name}`\n\nDefined in `{match.Object.SourceFile}:{match.Object.Line}`";
        if (signature.Length > 0)
        {
            value += "\n\n`(" + signature + ")`";
        }

        return new { contents = new { kind = "markdown", value } };
    }

    private static object SuppressLintAction(string uri, LintDiagnostic diagnostic)
    {
        return QuickFix(
            $"Disable sqldeps '{diagnostic.Rule}' for this line",
            uri,
            new(new(diagnostic.Range.Start.Line, 0), new(diagnostic.Range.Start.Line, 0)),
            $"-- sqldeps: disable-next-line={diagnostic.Rule}\n"
        );
    }

    private static object? NullComparisonAction(string uri, string? text, LintDiagnostic diagnostic)
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

        return QuickFix($"Replace with {replacement}", uri, diagnostic.Range, replacement);
    }

    private static object? AliasReplacementAction(string uri, string? text, LintDiagnostic diagnostic)
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

        return QuickFix($"Replace table name with alias '{alias}'", uri, diagnostic.Range, replacement);
    }

    private static IEnumerable<object> AmbiguousColumnActions(string uri, string? text, LintDiagnostic diagnostic)
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
            .Select(candidate => QuickFix($"Qualify with '{candidate}'", uri, diagnostic.Range, $"{candidate}.{column}"));
    }

    private static IReadOnlyList<string> AmbiguousColumnCandidates(string message)
    {
        var match = AmbiguousColumnCandidatesPattern().Match(message);

        return match.Success ? match.Groups["candidates"].Value.Split(", ") : [];
    }

    private static object QuickFix(string title, string uri, LspRange range, string newText) => new
    {
        title,
        kind = "quickfix",
        edit = new
        {
            changes = new Dictionary<string, object>
            {
                [uri] = new[] { new { range, newText } }
            }
        }
    };

    private static bool TryAliasReplacement(string reference, string message, out string replacement, out string alias)
    {
        var aliasMatch = AliasInDiagnosticPattern().Match(message);
        var separator = reference.LastIndexOf('.');

        if (!aliasMatch.Success || separator <= 0 || separator == reference.Length - 1)
        {
            replacement = "";
            alias = "";
            return false;
        }

        alias = aliasMatch.Groups["alias"].Value;
        replacement = alias + reference[separator..];

        return true;
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
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.Objects.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.Objects))
                .Select(item => (item, UriFromPath(item.SourceFile)))
                .ToArray();
        }
    }

    private IEnumerable<Dependency> AllDependencies()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.Dependencies.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.Dependencies)).ToArray();
        }
    }

    private IEnumerable<ObjectParameter> AllParameters()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.Parameters.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.Parameters)).ToArray();
        }
    }

    private IEnumerable<TableColumn> AllTableColumns()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.TableColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.TableColumns)).ToArray();
        }
    }

    private IEnumerable<ResultColumn> AllResultColumns()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.ResultColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.ResultColumns)).ToArray();
        }
    }

    private IEnumerable<TableKey> AllTableKeys()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.TableKeys.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.TableKeys)).ToArray();
        }
    }

    private IEnumerable<TableIndexColumn> AllIndexColumns()
    {
        lock (gate)
        {
            var openPaths = openDocuments.Values.Select(document => document.SourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = workspace?.IndexColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
            return saved.Concat(openDocuments.Values.SelectMany(document => document.IndexColumns)).ToArray();
        }
    }

    private void LoadWorkspace(int loadVersion)
    {
        try
        {
            if (workspaceRoot is null || !Directory.Exists(workspaceRoot))
            {
                return;
            }

            SqlFolderIndexer indexer = new(ObjectNames.StandardDefaultSchema, []);
            indexer.Index(workspaceRoot);
            AnalysisResult loaded = new(workspaceRoot);
            loaded.Objects.AddRange(indexer.Objects.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.Dependencies.AddRange(indexer.Dependencies.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.Parameters.AddRange(indexer.Parameters.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.ResultColumns.AddRange(indexer.ResultColumns.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.TableKeys.AddRange(indexer.TableKeys.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.IndexColumns.AddRange(indexer.IndexColumns.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.TableColumns.AddRange(indexer.TableColumns.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.Problems.AddRange(indexer.Problems.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));
            loaded.LintFindings.AddRange(indexer.LintFindings.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));

            if (loadVersion != Volatile.Read(ref workspaceLoadVersion))
            {
                return;
            }

            lock (gate)
            {
                if (loadVersion != workspaceLoadVersion)
                {
                    return;
                }

                workspace = loaded;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: workspace index failed: " + exception.Message);
        }
    }

    private string AbsoluteSource(string relativePath) => Path.GetFullPath(Path.Combine(workspaceRoot!, relativePath));

    private object WorkspaceDiagnostics()
    {
        lock (gate)
        {
            if (workspace is null)
            {
                return new { items = Array.Empty<object>() };
            }

            var groups = WorkspaceProblemGroups(workspace)
                .Concat(WorkspaceLintGroups(workspace))
                .GroupBy(group => group.Uri, StringComparer.Ordinal)
                .Select(group => new
                {
                    uri = group.Key,
                    version = (int?)null,
                    kind = "full",
                    items = group.SelectMany(item => item.Diagnostics).ToArray(),
                });

            return new { items = groups.ToArray() };
        }
    }

    private IEnumerable<WorkspaceDiagnosticGroup> WorkspaceProblemGroups(AnalysisResult loaded)
    {
        return loaded.Problems
            .GroupBy(problem => problem.SourceFile, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkspaceDiagnosticGroup(
                UriFromPath(group.Key),
                group.DistinctBy(problem => (problem.Line, problem.Column, problem.Message))
                    .Select(problem => (object)new
                    {
                        range = Range(problem.Line, problem.Column, 1),
                        severity = 1,
                        source = "sqldeps",
                        code = (string?)null,
                        data = new { source = "fresh-index", confidence = "high" },
                        message = problem.Message,
                    }).ToArray()));
    }

    private IEnumerable<WorkspaceDiagnosticGroup> WorkspaceLintGroups(AnalysisResult loaded)
    {
        return loaded.LintFindings
            .GroupBy(finding => finding.SourceFile, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkspaceDiagnosticGroup(
                UriFromPath(group.Key),
                group.DistinctBy(finding => (finding.Rule, finding.Line, finding.Column, finding.Message))
                    .Select(finding => (object)new
                    {
                        range = Range(finding.Line, finding.Column, finding.Length),
                        severity = DiagnosticSeverity(finding.Severity),
                        source = "sqldeps",
                        code = finding.Rule,
                        data = new { source = "fresh-index", confidence = DiagnosticConfidence(finding.Rule) },
                        message = finding.Message,
                    }).ToArray()));
    }

    private void RefreshWorkspaceFile(string path)
    {
        if (!IsWorkspaceSqlFile(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var refreshed = DocumentAnalysis.Analyze(fullPath, SqlFileReader.Decode(File.ReadAllBytes(fullPath)));
            lock (gate)
            {
                if (workspace is null)
                {
                    return;
                }

                workspace.Objects.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.Dependencies.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.Parameters.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.ResultColumns.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.TableKeys.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.IndexColumns.RemoveAll(item => SamePath(item.SourceFile, fullPath));
                workspace.TableColumns.RemoveAll(item => SamePath(item.SourceFile, fullPath));

                workspace.Objects.AddRange(refreshed.Objects);
                workspace.Dependencies.AddRange(refreshed.Dependencies);
                workspace.Parameters.AddRange(refreshed.Parameters);
                workspace.ResultColumns.AddRange(refreshed.ResultColumns);
                workspace.TableKeys.AddRange(refreshed.TableKeys);
                workspace.IndexColumns.AddRange(refreshed.IndexColumns);
                workspace.TableColumns.AddRange(refreshed.TableColumns);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: workspace file refresh failed: " + exception.Message);
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

    private string? NameAt(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var position = parameters.GetProperty("position");
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        AnalysisResult? document;
        lock (gate)
        {
            openDocuments.TryGetValue(uri, out document);
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
            openDocuments.TryGetValue(uri, out var document);
            return (document, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        }
    }

    private AnalysisResult? OpenDocument(string uri)
    {
        lock (gate)
        {
            return openDocuments.GetValueOrDefault(uri);
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
        "table" => 23,
        "view" => 5,
        "procedure" or "function" or "trigger" => 12,
        _ => 19,
    };

    private static int DiagnosticSeverity(string severity) => severity switch
    {
        "error" => 1,
        "warning" => 2,
        _ => 3,
    };

    private static string DiagnosticConfidence(string rule) => rule switch
    {
        "implicit-filter-conversion"
            or "implicit-conversion"
            or "leading-wildcard-like"
            or "non-sargable-function"
            or "merge"
            or "merge-nondeterministic-match"
            or "transaction-without-error-handling"
            or "nolock"
            or "nolock-in-transaction"
            or "unfiltered-select"
            or "insert-exec-count"
            or "unindexed-where" => "low",
        _ => "medium",
    };

    private static LspLocation Location(string path, int line) => new(UriFromPath(path), Range(line));

    private static LspRange Range(int oneBasedLine) => new(new(Math.Max(0, oneBasedLine - 1), 0), new(Math.Max(0, oneBasedLine - 1), 1));

    private static LspRange Range(int oneBasedLine, int oneBasedColumn, int length)
    {
        var line = Math.Max(0, oneBasedLine - 1);
        var start = Math.Max(0, oneBasedColumn - 1);
        return new(new(line, start), new(line, start + Math.Max(1, length)));
    }

    private static string? WorkspaceRoot(JsonElement parameters)
    {
        if (TryGetPath(parameters, "rootUri", out var rootUri)
            || TryGetPath(parameters, "rootPath", out rootUri))
        {
            return rootUri;
        }

        if (!parameters.TryGetProperty("workspaceFolders", out var folders)
            || folders.ValueKind != JsonValueKind.Array
            || folders.GetArrayLength() == 0)
        {
            return null;
        }

        return TryGetPath(folders[0], "uri", out var folderUri) ? folderUri : null;
    }

    private static bool TryGetPath(JsonElement value, string propertyName, out string? path)
    {
        if (!TryGetPathString(value, propertyName, out var text))
        {
            path = null;

            return false;
        }

        path = propertyName.EndsWith("Uri", StringComparison.Ordinal) ? PathFromUri(text) : text;

        return true;
    }

    private static bool TryGetPathString(JsonElement value, string propertyName, out string text)
    {
        if (value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } result)
        {
            text = result;

            return true;
        }

        text = "";

        return false;
    }

    private static string PathFromUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            return parsed.LocalPath;
        }

        return uri;
    }

    private static string UriFromPath(string path)
    {
        var uri = new Uri(Path.GetFullPath(path));
        return uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
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

    private sealed record LintDiagnostic(string Rule, LspRange Range, string Message);
    private sealed record WorkspaceDiagnosticGroup(string Uri, object[] Diagnostics);
}
