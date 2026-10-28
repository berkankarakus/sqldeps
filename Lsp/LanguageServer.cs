using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Serves SQL dependency navigation to an LSP client such as Neovim.</summary>
public sealed class LanguageServer
{
    private readonly JsonRpcConnection connection = new(Console.OpenStandardInput(), Console.OpenStandardOutput());
    private readonly Dictionary<string, AnalysisResult> openDocuments = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private AnalysisResult? workspace;
    private string? workspaceRoot;
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
        object? result = method switch
        {
            "initialize" => Initialize(parameters),
            "shutdown" => Shutdown(),
            "textDocument/documentSymbol" => DocumentSymbols(parameters),
            "workspace/symbol" => WorkspaceSymbols(parameters),
            "textDocument/definition" => Definitions(parameters),
            "textDocument/references" => References(parameters),
            "textDocument/hover" => Hover(parameters),
            "textDocument/completion" => Completion(parameters),
            _ => null,
        };

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

        if (hasId)
        {
            connection.Respond(id, result);
        }

        return false;
    }

    private object Initialize(JsonElement parameters)
    {
        workspaceRoot = WorkspaceRoot(parameters);
        if (workspaceRoot is not null)
        {
            _ = Task.Run(LoadWorkspace);
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
        var analysis = DocumentAnalysis.Analyze(PathFromUri(uri), text);
        lock (gate)
        {
            openDocuments[uri] = analysis;
        }

        connection.Notify("textDocument/publishDiagnostics", new
        {
            uri,
            diagnostics = analysis.Problems.Select(problem => new
            {
                range = Range(problem.Line),
                severity = 1,
                source = "sqldeps",
                message = problem.Message,
            }),
        });
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

    private object Completion(JsonElement parameters)
    {
        var (alias, prefix) = CompletionContext(parameters);
        if (alias is null || prefix is null || !TryResolveAlias(parameters, alias, out var table))
        {
            return new { isIncomplete = false, items = Array.Empty<object>() };
        }

        var items = AllTableColumns()
            .Where(column => SameName(column.TableName, table) && column.ColumnName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(column => new { label = column.ColumnName, kind = 5, detail = column.DataType, documentation = new { kind = "markdown", value = $"`{column.TableName}.{column.ColumnName}`" } })
            .ToArray();
        return new { isIncomplete = false, items };
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

    private void LoadWorkspace()
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
            loaded.TableColumns.AddRange(indexer.TableColumns.Select(item => item with { SourceFile = AbsoluteSource(item.SourceFile) }));

            lock (gate)
            {
                workspace = loaded;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: workspace index failed: " + exception.Message);
        }
    }

    private string AbsoluteSource(string relativePath) => Path.GetFullPath(Path.Combine(workspaceRoot!, relativePath));

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
                workspace.TableColumns.RemoveAll(item => SamePath(item.SourceFile, fullPath));

                workspace.Objects.AddRange(refreshed.Objects);
                workspace.Dependencies.AddRange(refreshed.Dependencies);
                workspace.Parameters.AddRange(refreshed.Parameters);
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
        if (workspaceRoot is null || !path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(workspaceRoot, Path.GetFullPath(path));
        return relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relativePath);
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

    private (string? Alias, string? Prefix) CompletionContext(JsonElement parameters)
    {
        var (document, line, character) = DocumentAt(parameters);
        if (document is null) return (null, null);
        var lines = document.Text.Split('\n');
        if (line < 0 || line >= lines.Length) return (null, null);
        var beforeCursor = lines[line].TrimEnd('\r')[..Math.Min(character, lines[line].TrimEnd('\r').Length)];
        var match = Regex.Match(beforeCursor, @"(?<alias>[\w\[\]]+)\.(?<prefix>[\w\[\]]*)$");
        return match.Success ? (match.Groups["alias"].Value.Trim('[', ']'), match.Groups["prefix"].Value.Trim('[', ']')) : (null, null);
    }

    private bool TryResolveAlias(JsonElement parameters, string alias, out string table)
    {
        var (document, line, _) = DocumentAt(parameters);
        if (document is null) { table = ""; return false; }
        var beforeCursor = string.Join('\n', document.Text.Split('\n').Take(line + 1));
        var matches = Regex.Matches(beforeCursor, @"\b(?:FROM|JOIN)\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,3})(?:\s+(?:AS\s+)?(?<alias>\w+))?", RegexOptions.IgnoreCase);
        foreach (Match match in matches.Reverse())
        {
            var writtenAlias = match.Groups["alias"].Success ? match.Groups["alias"].Value : match.Groups["table"].Value.Split('.').Last().Trim('[', ']');
            if (string.Equals(writtenAlias, alias, StringComparison.OrdinalIgnoreCase))
            {
                table = Qualify(match.Groups["table"].Value.Replace("[", "").Replace("]", ""));
                return true;
            }
        }

        table = "";
        return false;
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

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '[' or ']';

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

    private static LspLocation Location(string path, int line) => new(UriFromPath(path), Range(line));

    private static LspRange Range(int oneBasedLine) => new(new(Math.Max(0, oneBasedLine - 1), 0), new(Math.Max(0, oneBasedLine - 1), 1));

    private static string? WorkspaceRoot(JsonElement parameters)
    {
        if (parameters.TryGetProperty("rootUri", out var rootUri) && rootUri.ValueKind == JsonValueKind.String && rootUri.GetString() is { } uri)
        {
            return PathFromUri(uri);
        }

        return null;
    }

    private static string PathFromUri(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile
        ? parsed.LocalPath
        : uri;

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
}
