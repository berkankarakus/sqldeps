using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

/// <summary>Shares LSP code-action preview composition across protocol and CLI adapters.</summary>
internal static class CodeActionPreviewService
{
    internal static object[] Build(
        IndexAnalysisMetadata indexed,
        string sourceFile,
        string text,
        string defaultSchema)
    {
        return LanguageServer.PreviewCodeActions(indexed, sourceFile, text, defaultSchema);
    }

    internal static RefactorCandidate[] BuildRefactorCandidates(
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

        var analysis = DocumentAnalysis.Analyze(
            sourceFile,
            text,
            defaultSchema,
            indexed.DefaultDatabase);
        var actions = LanguageServer.PreviewCodeActions(
            indexed,
            sourceFile,
            text,
            defaultSchema,
            analysis);

        return RefactorCandidatePreviewService.Build(
            actions,
            analysis.LintFindings,
            analysis.Dependencies,
            queries.IsIndexCurrent,
            name => queries.ObjectQuery("impact", name),
            sourceFile,
            text,
            defaultSchema);
    }
}
