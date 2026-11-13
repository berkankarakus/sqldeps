using System.Text;
using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddRepositoryContextAction(
        List<object> actions,
        JsonElement parameters,
        string? text)
    {
        if (!TryGetSelectedObjectName(parameters, text, out var selectedName))
        {
            return;
        }

        var objectName = Qualify(selectedName.Trim().Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, objectName));

        if (match.Object is null)
        {
            return;
        }

        var title = $"Show repository context for {match.Object.Name}";
        actions.Add(new
        {
            title,
            kind = "source",
            data = new
            {
                source = "sqldeps",
                confidence = "high",
                preview = true,
                writesFile = false,
                applicability = "The selected text must resolve to one indexed object.",
                generation = "repository-context",
                rule = "repository-context",
            },
            command = new
            {
                title,
                command = "sqldeps.repository-context",
                arguments = new[] { match.Object.Name },
            },
        });
    }

    private string ExecuteRepositoryContextCommand(JsonElement parameters)
    {
        if (!TryGetCommandObjectName(parameters, out var objectName))
        {
            throw new ArgumentException("sqldeps.repository-context requires one object name argument.");
        }

        var qualifiedName = Qualify(objectName.Trim().Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, qualifiedName));

        if (match.Object is null)
        {
            throw new ArgumentException($"No indexed object: {qualifiedName}");
        }

        return FormatRepositoryContext(match.Object.Name, match.Object.Kind, match.Object.SourceFile, match.Object.Line);
    }

    private string FormatRepositoryContext(
        string objectName,
        string objectKind,
        string sourceFile,
        int line)
    {
        var dependencies = AllDependencies().ToArray();
        var outgoing = dependencies
            .Where(dependency => SameName(dependency.SourceObject, objectName))
            .ToArray();
        var incoming = dependencies
            .Where(dependency => SameName(dependency.TargetObject, objectName))
            .ToArray();
        var reachability = RepositoryReachability(dependencies);
        var impactRows = ReverseImpactRows(objectName, reachability);

        StringBuilder output = new();

        output.AppendLine($"Repository context: {objectName}");
        output.AppendLine($"Kind: {objectKind}");
        output.AppendLine($"Defined in: {sourceFile}:{line}");
        AppendRelationSection(output, "Uses", outgoing, dependency => dependency.TargetObject);
        AppendRelationSection(output, "Referenced by", incoming, dependency => dependency.SourceObject);
        AppendNameSection(output, "Affected by changes", impactRows.Select(row => row.Source));
        AppendCycleSection(output, objectName, reachability, impactRows);
        AppendTraversalLimitSection(output, impactRows);

        return output.ToString().TrimEnd();
    }

    private static void AppendRelationSection(
        StringBuilder output,
        string title,
        IEnumerable<Dependency> dependencies,
        Func<Dependency, string> nameSelector)
    {
        var relations = dependencies
            .Select(dependency => $"{nameSelector(dependency)} ({dependency.Operation})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRepositoryContextNames)
            .ToArray();

        output.AppendLine($"{title}: {FormatNames(relations)}");
    }

    private static void AppendNameSection(
        StringBuilder output,
        string title,
        IEnumerable<string> names)
    {
        var values = names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRepositoryContextNames)
            .ToArray();

        output.AppendLine($"{title}: {FormatNames(values)}");
    }

    private static string FormatNames(IReadOnlyList<string> names)
    {
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static IReadOnlyList<ReachRow> ReverseImpactRows(
        string objectName,
        ReachResult reachability)
    {
        return reachability
            .Reach
            .Where(row => SameName(row.Reached, objectName))
            .OrderBy(row => row.Depth)
            .ThenBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ReachResult RepositoryReachability(IReadOnlyList<Dependency> dependencies)
    {
        return ReachabilityBuilder.Build(dependencies, MaxRepositoryTraversalDepth);
    }

    private static void AppendCycleSection(
        StringBuilder output,
        string objectName,
        ReachResult reachability,
        IReadOnlyList<ReachRow> impactRows)
    {
        var affectedObjects = impactRows
            .Select(row => row.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cycles = reachability.Cycles
            .Where(cycle => SameName(cycle.ObjectName, objectName) || affectedObjects.Contains(cycle.ObjectName))
            .Select(cycle => $"{cycle.ObjectName} (depth {cycle.Depth})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRepositoryContextNames)
            .ToArray();

        if (cycles.Length > 0)
        {
            output.AppendLine($"Cycles: {FormatNames(cycles)}");
        }
    }

    private static void AppendTraversalLimitSection(
        StringBuilder output,
        IReadOnlyList<ReachRow> impactRows)
    {
        if (impactRows.Any(row => row.Depth >= MaxRepositoryTraversalDepth))
        {
            output.AppendLine($"Caller traversal: may be truncated at depth {MaxRepositoryTraversalDepth}; use callers --depth for a wider search.");
        }
    }

    private string RepositoryContextHover(string objectName)
    {
        var dependencies = AllDependencies().ToArray();
        var outgoing = dependencies.Count(dependency => SameName(dependency.SourceObject, objectName));
        var incoming = dependencies.Count(dependency => SameName(dependency.TargetObject, objectName));
        var reachability = RepositoryReachability(dependencies);
        var impactRows = ReverseImpactRows(objectName, reachability);
        var impact = impactRows.Count;
        var cycleCount = reachability.Cycles.Count(cycle =>
            SameName(cycle.ObjectName, objectName)
            || impactRows.Any(row => SameName(row.Source, cycle.ObjectName)));
        var depthLimited = impactRows.Any(row => row.Depth >= MaxRepositoryTraversalDepth);

        if (outgoing == 0 && incoming == 0 && impact == 0 && cycleCount == 0 && !depthLimited)
        {
            return "";
        }

        StringBuilder output = new();
        output.Append("\n\n**Repository context**\n");
        output.Append($"- Uses: `{outgoing}` relationship(s)\n");
        output.Append($"- Referenced by: `{incoming}` relationship(s)\n");
        output.Append($"- Affected by changes: `{impact}` object(s)");

        if (cycleCount > 0)
        {
            output.Append($"\n- Cycles: `{cycleCount}`");
        }

        if (depthLimited)
        {
            output.Append($"\n- Caller traversal may be truncated at depth `{MaxRepositoryTraversalDepth}`");
        }

        return output.ToString();
    }
}
