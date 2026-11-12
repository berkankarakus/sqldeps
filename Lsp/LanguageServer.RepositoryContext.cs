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
        var impact = ReverseImpact(objectName, dependencies);

        StringBuilder output = new();

        output.AppendLine($"Repository context: {objectName}");
        output.AppendLine($"Kind: {objectKind}");
        output.AppendLine($"Defined in: {sourceFile}:{line}");
        AppendRelationSection(output, "Uses", outgoing, dependency => dependency.TargetObject);
        AppendRelationSection(output, "Referenced by", incoming, dependency => dependency.SourceObject);
        AppendNameSection(output, "Affected by changes", impact);

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

    private static IReadOnlyList<string> ReverseImpact(
        string objectName,
        IReadOnlyList<Dependency> dependencies)
    {
        return ReverseImpactRows(objectName, dependencies)
            .Select(row => row.Source)
            .ToArray();
    }

    private static IReadOnlyList<ReachRow> ReverseImpactRows(
        string objectName,
        IReadOnlyList<Dependency> dependencies)
    {
        return ReachabilityBuilder.Build(dependencies, 12)
            .Reach
            .Where(row => SameName(row.Reached, objectName))
            .OrderBy(row => row.Depth)
            .ThenBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string RepositoryContextHover(string objectName)
    {
        var dependencies = AllDependencies().ToArray();
        var outgoing = dependencies.Count(dependency => SameName(dependency.SourceObject, objectName));
        var incoming = dependencies.Count(dependency => SameName(dependency.TargetObject, objectName));
        var impact = ReverseImpact(objectName, dependencies).Count;

        if (outgoing == 0 && incoming == 0 && impact == 0)
        {
            return "";
        }

        return "\n\n**Repository context**\n"
            + $"- Uses: `{outgoing}` relationship(s)\n"
            + $"- Referenced by: `{incoming}` relationship(s)\n"
            + $"- Affected by changes: `{impact}` object(s)";
    }
}
