using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Core;

/// <summary>Projects validated LSP action payloads into safe refactor candidates.</summary>
public static class RefactorCandidateProjector
{
    /// <summary>Projects safe candidates without dependency-impact enrichment.</summary>
    /// <param name="actions">LSP code actions returned for the current document.</param>
    public static RefactorCandidate[] Project(IEnumerable<JsonElement> actions)
    {
        return Project(actions, null);
    }

    /// <summary>Projects safe candidates and attaches static dependency impact when available.</summary>
    /// <param name="actions">LSP code actions returned for the current document.</param>
    /// <param name="affectedDependencies">Precomputed impact for the candidate's source object.</param>
    public static RefactorCandidate[] Project(
        IEnumerable<JsonElement> actions,
        RefactorDependencyImpact? affectedDependencies
    )
    {
        var candidates = new List<RefactorCandidate>();

        foreach (var action in actions)
        {
            if (!TryCreateCandidate(action, affectedDependencies, out var candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        return candidates.ToArray();
    }

    private static bool TryCreateCandidate(
        JsonElement action,
        RefactorDependencyImpact? affectedDependencies,
        out RefactorCandidate candidate
    )
    {
        candidate = null!;

        if (!RefactorCandidateActionReader.TryRead(action, out var actionData))
        {
            return false;
        }

        RefactorDependencyImpact impact = affectedDependencies ?? EmptyDependencyImpact();

        candidate = new RefactorCandidate(
            actionData.Title,
            actionData.Kind,
            actionData.Rule,
            actionData.Confidence,
            actionData.Applicability,
            actionData.References,
            impact,
            actionData.Preview,
            WritesFile: false);

        return true;
    }

    private static RefactorDependencyImpact EmptyDependencyImpact()
    {
        return new RefactorDependencyImpact("complete", null, []);
    }
}
