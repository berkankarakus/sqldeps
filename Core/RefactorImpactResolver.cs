namespace SqlDeps.Core;

/// <summary>Resolves static dependency impact for a refactor candidate.</summary>
public static class RefactorImpactResolver
{
    /// <summary>Returns distinct static dependency targets, excluding dynamic SQL edges.</summary>
    /// <param name="dependencies">Dependencies discovered for one source object.</param>
    public static string[] StaticDependencyTargets(IEnumerable<Dependency> dependencies)
    {
        return dependencies
            .Where(dependency => !dependency.ViaDynamic && !string.IsNullOrWhiteSpace(dependency.TargetObject))
            .Select(dependency => dependency.TargetObject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Resolves dependency impact while marking dynamic SQL as a partial result.</summary>
    /// <param name="dependencies">Dependencies discovered for one source object.</param>
    /// <param name="queries">Read-only index query service used for impact traversal.</param>
    public static RefactorDependencyImpact Resolve(
        IEnumerable<Dependency> dependencies,
        IndexQueryService queries)
    {
        var dependencyList = dependencies.ToArray();
        var targets = StaticDependencyTargets(dependencyList);
        var items = targets
            .Select(target => new RefactorDependencyItem(
                target,
                queries.ObjectQuery("impact", target)
                    .Select(ToImpactRow)
                    .ToArray()))
            .ToArray();
        var hasDynamicDependency = dependencyList.Any(dependency => dependency.ViaDynamic);
        var status = hasDynamicDependency ? "partial" : "complete";
        string? reason = hasDynamicDependency
            ? "Dynamic SQL dependencies were not resolved."
            : null;
        RefactorGraphSummary graph = GraphSummary(targets, items);

        return new RefactorDependencyImpact(
            status,
            reason,
            items,
            graph);
    }

    private static RefactorGraphSummary GraphSummary(
        IReadOnlyCollection<string> targets,
        IReadOnlyCollection<RefactorDependencyItem> items)
    {
        RefactorImpactRow[] impactRows = items.SelectMany(item => item.Impact).ToArray();
        int affectedObjectCount = impactRows
            .Select(row => row.Affected)
            .Where(affected => !string.IsNullOrWhiteSpace(affected))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int maximumDepth = impactRows
            .Where(row => row.Depth is not null)
            .Select(row => row.Depth!.Value)
            .DefaultIfEmpty()
            .Max();

        return new RefactorGraphSummary(targets.Count, affectedObjectCount, maximumDepth);
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
        if (!row.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is null)
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
