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
        var actionElements = actions.Select(action => JsonSerializer.SerializeToElement(action, JsonOptions));
        var candidates = RefactorCandidateProjector.Project(actionElements);

        return OrderCandidates(candidates
            .Where(candidate => IsParseValidPreview(candidate, sourceFile, text, defaultSchema))
            .Select(candidate => AddDiagnosticContext(candidate, analysis, queries))
            .Where(IsSafeCandidate));
    }

    internal static bool IsSafeCandidate(RefactorCandidate candidate)
    {
        return !candidate.WritesFile
            && string.Equals(
                candidate.AffectedDependencies.Status,
                "complete",
                StringComparison.OrdinalIgnoreCase);
    }

    internal static RefactorCandidate[] OrderCandidates(IEnumerable<RefactorCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => ConfidenceRank(candidate.Confidence))
            .ThenByDescending(IsSafeCandidate)
            .ThenByDescending(candidate => candidate.References.Count)
            .ThenByDescending(candidate => candidate.AffectedDependencies.Items.Sum(item => item.Impact.Count))
            .ThenBy(candidate => candidate.Rule, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ConfidenceRank(string confidence)
    {
        return confidence.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0,
        };
    }

    internal static bool IsParseValidPreview(
        RefactorCandidate candidate,
        string sourceFile,
        string text,
        string defaultSchema)
    {
        if (!TryApplyPreview(candidate, sourceFile, text, out var previewText))
        {
            return false;
        }

        var previewAnalysis = DocumentAnalysis.Analyze(sourceFile, previewText, defaultSchema);

        return previewAnalysis.Problems.Count == 0;
    }

    internal static bool TryApplyPreview(
        RefactorCandidate candidate,
        string sourceFile,
        string text,
        out string previewText)
    {
        previewText = text;
        var expectedUri = PreviewUri(sourceFile);

        if (candidate.Preview.Changes.Count != 1
            || !candidate.Preview.Changes.TryGetValue(expectedUri, out var edits)
            || edits.Count == 0)
        {
            return false;
        }

        var positionedEdits = new List<AppliedPreviewEdit>();

        foreach (var edit in edits)
        {
            if (!TryGetOffset(text, edit.Range.Start, out var start)
                || !TryGetOffset(text, edit.Range.End, out var end)
                || start > end)
            {
                return false;
            }

            positionedEdits.Add(new AppliedPreviewEdit(start, end, edit.NewText));
        }

        var ascendingEdits = positionedEdits
            .OrderBy(edit => edit.Start)
            .ThenBy(edit => edit.End)
            .ToArray();

        for (var index = 1; index < ascendingEdits.Length; index++)
        {
            var previous = ascendingEdits[index - 1];
            var current = ascendingEdits[index];

            if (current.Start < previous.End || current.Start == previous.Start)
            {
                return false;
            }
        }

        foreach (var edit in ascendingEdits.Reverse())
        {
            previewText = previewText[..edit.Start] + edit.NewText + previewText[edit.End..];
        }

        return true;
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

    internal static RefactorCandidate AddDiagnosticContext(
        RefactorCandidate candidate,
        AnalysisResult analysis,
        IndexQueryService? queries)
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

        if (queries is null)
        {
            return candidate with
            {
                SourceObject = sourceObject,
                AffectedDependencies = new RefactorDependencyImpact(
                    "unavailable",
                    "No selected or automatic index is available for dependency impact.",
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

    private static bool TryGetOffset(
        string text,
        RefactorPosition position,
        out int offset)
    {
        offset = 0;
        var lines = text.Split('\n');

        if (position.Line < 0 || position.Line >= lines.Length || position.Character < 0)
        {
            return false;
        }

        var line = lines[position.Line].TrimEnd('\r');

        if (position.Character > line.Length)
        {
            return false;
        }

        offset = lines
            .Take(position.Line)
            .Sum(item => item.Length + 1) + position.Character;

        return true;
    }

    private static string PreviewUri(string sourceFile)
    {
        if (Path.IsPathRooted(sourceFile))
        {
            return LspUri.FromPath(sourceFile);
        }

        return LspUri.FromPath(Path.Combine(Path.GetTempPath(), "sqldeps-mcp-preview.sql"));
    }

    private sealed record AppliedPreviewEdit(int Start, int End, string NewText);
}
