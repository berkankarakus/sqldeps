using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    internal static object[] PreviewCodeActions(
        IndexAnalysisMetadata indexed,
        string sourceFile,
        string text,
        string defaultSchema,
        AnalysisResult? analyzedDocument = null
    )
    {
        var server = new LanguageServer();
        var uri = PreviewUri(sourceFile);
        var indexedWorkspace = IndexedWorkspace(indexed);
        var document = analyzedDocument ?? DocumentAnalysis.Analyze(sourceFile, text, defaultSchema);

        server.workspace = indexedWorkspace;
        server.experimentalSuggestions = true;
        server.StoreDocument(uri, document, version: null, allowSameVersion: true);
        server.AddLiveDocumentFindings(document);

        var diagnostics = server.DiagnosticsFor(document, "live-document");
        using var request = JsonSerializer.SerializeToDocument(new
        {
            textDocument = new
            {
                uri
            },
            range = FullDocumentRange(text),
            context = new
            {
                diagnostics
            },
        });

        return server.CodeActions(request.RootElement) is IEnumerable<object> actions
            ? actions.ToArray()
            : [];
    }

    private static AnalysisResult IndexedWorkspace(IndexAnalysisMetadata indexed)
    {
        AnalysisResult workspace = new("(mcp-index)");
        workspace.Objects.AddRange(indexed.Objects);
        workspace.Parameters.AddRange(indexed.Parameters);
        workspace.ResultColumns.AddRange(indexed.Catalog.ResultColumns);
        workspace.TableColumns.AddRange(indexed.Catalog.TableColumns);
        workspace.TableKeys.AddRange(indexed.Catalog.TableKeys);
        workspace.ForeignKeys.AddRange(indexed.Catalog.ForeignKeys);
        workspace.IndexColumns.AddRange(indexed.Catalog.IndexColumns);

        return workspace;
    }

    private static string PreviewUri(string sourceFile)
    {
        if (Path.IsPathRooted(sourceFile))
        {
            return LspUri.FromPath(sourceFile);
        }

        return LspUri.FromPath(Path.Combine(Path.GetTempPath(), "sqldeps-mcp-preview.sql"));
    }

    private static LspRange FullDocumentRange(string text)
    {
        var lines = text.Split('\n');
        var lastLine = lines.Length - 1;
        var lastCharacter = lines[lastLine].TrimEnd('\r').Length;

        return new LspRange(new(0, 0), new(lastLine, lastCharacter));
    }
}
