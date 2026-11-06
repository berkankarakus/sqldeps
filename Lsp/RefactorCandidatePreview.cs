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
        var candidates = RefactorCandidateProjector.Project(actionElements);

        return candidates
            .Select(candidate => AddDiagnosticContext(candidate, analysis, queries))
            .ToArray();
    }

    internal static string? FindSourceObject(
        RefactorCandidate candidate,
        IReadOnlyList<LintFinding> findings)
    {
        RefactorTextEdit? firstEdit = candidate.Preview.Changes.Values
            .SelectMany(edits => edits)
            .FirstOrDefault();

        if (firstEdit is null)
        {
            return null;
        }

        LintFinding? finding = findings
            .Where(item => string.Equals(item.Rule, candidate.Rule, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => Math.Abs(item.Line - 1 - firstEdit.Range.Start.Line))
            .ThenBy(item => item.Line)
            .FirstOrDefault();

        if (finding is null || string.IsNullOrWhiteSpace(finding.SourceObject))
        {
            return null;
        }

        return finding.SourceObject;
    }

    private static RefactorCandidate AddDiagnosticContext(
        RefactorCandidate candidate,
        AnalysisResult analysis,
        IndexQueryService queries)
    {
        var sourceObject = FindSourceObject(candidate, analysis.LintFindings);

        if (sourceObject is null)
        {
            return candidate with
            {
                AffectedDependencies = new RefactorDependencyImpact(
                    "unavailable",
                    "No matching diagnostic source object was found.",
                    []),
            };
        }

        var dependencies = analysis.Dependencies
            .Where(dependency => string.Equals(
                dependency.SourceObject,
                sourceObject,
                StringComparison.OrdinalIgnoreCase));
        var dependencyImpact = RefactorImpactResolver.Resolve(dependencies, queries);

        return candidate with
        {
            SourceObject = sourceObject,
            AffectedDependencies = dependencyImpact,
        };
    }
}
