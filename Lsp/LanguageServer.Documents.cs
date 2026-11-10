using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
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
            diagnosticCache.Remove(uri);
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

        var analysis = AnalyzeDocument(sourceFile, text, workspaceAnalysisRoot);
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
            diagnostics = DiagnosticsForDocument(uri, analysis, "live-document"),
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

        analysis.LintFindings.AddRange(QueryColumnValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, metadata, analysis.LocalColumns, analysis.Text, workspaceAnalysisRoot));
        analysis.LintFindings.AddRange(ProcedureParameterValidator.Validate(analysis.SourceFile, analysis.ParsedBatches, AllObjects().Select(item => item.Object), AllParameters(), analysis.Text, workspaceAnalysisRoot));
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
}
