using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Lsp;

namespace SqlDeps.Preview;

/// <summary>Composes host-level preview operations from the LSP action engine.</summary>
internal static class PreviewService
{
    public static object[] CodeActions(
        IndexAnalysisMetadata indexed,
        string sourceFile,
        string text,
        string defaultSchema)
    {
        return LanguageServer.PreviewCodeActions(indexed, sourceFile, text, defaultSchema);
    }

    public static RefactorCandidate[] RefactorCandidates(
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
            queries,
            sourceFile,
            text,
            defaultSchema);
    }
}
