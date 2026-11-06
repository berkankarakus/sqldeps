using System.Text.Json;
using SqlDeps.Core;

namespace SqlDeps.Lsp;

internal static class RefactorCandidatePreview
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static RefactorCandidate[] Build(
        IndexAnalysisMetadata indexed,
        IndexQueryService queries,
        string sourceFile,
        string text,
        string defaultSchema)
    {
        var actions = LanguageServer.PreviewCodeActions(
            indexed,
            sourceFile,
            text,
            defaultSchema);
        var actionElements = actions.Select(action => JsonSerializer.SerializeToElement(action, JsonOptions));
        var analysis = DocumentAnalysis.Analyze(sourceFile, text, defaultSchema);
        var dependencyImpact = RefactorImpactResolver.Resolve(analysis.Dependencies, queries);

        return RefactorCandidateProjector.Project(actionElements, dependencyImpact);
    }
}
