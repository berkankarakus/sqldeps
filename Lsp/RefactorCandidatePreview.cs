using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

/// <summary>Adapts LSP-generated actions to the shared Core preview validator.</summary>
internal static class RefactorCandidatePreview
{
    /// <summary>Generates LSP actions and delegates candidate validation to Core.</summary>
    internal static RefactorCandidate[] Build(
        IndexAnalysisMetadata indexed,
        IndexQueryService queries,
        string sourceFile,
        string text,
        string defaultSchema)
    {
        if (!queries.IsIndexCurrent())
        {
            return [];
        }

        var analysis = DocumentAnalysis.Analyze(sourceFile, text, defaultSchema);
        var actions = LanguageServer.PreviewCodeActions(
            indexed,
            sourceFile,
            text,
            defaultSchema,
            analysis);

        return RefactorCandidatePreviewService.Build(
            actions,
            analysis,
            queries.IsIndexCurrent,
            name => queries.ObjectQuery("impact", name),
            sourceFile,
            text,
            defaultSchema);
    }
}
