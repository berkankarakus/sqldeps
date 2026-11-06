using SqlDeps.Core;
using SqlDeps.Lsp;

namespace SqlDeps.Mcp;

internal static class RefactorImpactResolver
{
    internal static string[] StaticDependencyTargets(IEnumerable<Dependency> dependencies)
    {
        return dependencies
            .Where(dependency => !dependency.ViaDynamic && !string.IsNullOrWhiteSpace(dependency.TargetObject))
            .Select(dependency => dependency.TargetObject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static RefactorDependencyImpact Resolve(AnalysisResult analysis, IndexQueryService queries)
    {
        var targets = StaticDependencyTargets(analysis.Dependencies);
        var items = targets
            .Select(target => new RefactorDependencyItem(
                target,
                queries.ObjectQuery("impact", target)
                    .Select(ToImpactRow)
                    .ToArray()))
            .ToArray();
        var hasDynamicDependency = analysis.Dependencies.Any(dependency => dependency.ViaDynamic);
        var status = hasDynamicDependency ? "partial" : "complete";
        string? reason = hasDynamicDependency
            ? "Dynamic SQL dependencies were not resolved."
            : null;

        return new RefactorDependencyImpact(
            status,
            reason,
            items);
    }

    private static RefactorImpactRow ToImpactRow(IReadOnlyDictionary<string, object?> row)
    {
        return new RefactorImpactRow(
            ValueAsString(row, "affected"),
            ValueAsInt(row, "depth"),
            ValueAsString(row, "path"));
    }

    private static string? ValueAsString(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        string text = value.ToString()!;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? ValueAsInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        string? text = ValueAsString(row, key);

        if (text is null || !int.TryParse(text, out var value))
        {
            return null;
        }

        return value;
    }
}
