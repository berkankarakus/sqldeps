using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
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

    private static string Qualify(string name) => name.Contains('.') ? name : ObjectNames.StandardDefaultSchema + "." + name;
}
