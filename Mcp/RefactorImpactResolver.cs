using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Lsp;

namespace SqlDeps.Mcp;

internal static class RefactorImpactResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string[] StaticDependencyTargets(IEnumerable<Dependency> dependencies)
    {
        return dependencies
            .Where(dependency => !dependency.ViaDynamic && !string.IsNullOrWhiteSpace(dependency.TargetObject))
            .Select(dependency => dependency.TargetObject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static JsonElement Resolve(AnalysisResult analysis, IndexQueryService queries)
    {
        var targets = StaticDependencyTargets(analysis.Dependencies);
        var items = targets
            .Select(target => new
            {
                object_name = target,
                impact = queries.ObjectQuery("impact", target),
            })
            .ToArray();
        var hasDynamicDependency = analysis.Dependencies.Any(dependency => dependency.ViaDynamic);
        var status = hasDynamicDependency ? "partial" : "complete";
        string? reason = hasDynamicDependency
            ? "Dynamic SQL dependencies were not resolved."
            : null;

        return JsonSerializer.SerializeToElement(
            new
            {
                status,
                reason,
                items,
            },
            JsonOptions
        );
    }
}
