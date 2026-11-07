using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object PrepareCallHierarchy(JsonElement parameters)
    {
        var name = NameAt(parameters);
        if (name is null)
        {
            return Array.Empty<object>();
        }

        var match = FindObject(name);
        return match is null
            ? Array.Empty<object>()
            : new[] { CallHierarchyItem(match) };
    }

    private object IncomingCalls(JsonElement parameters)
    {
        if (!TryGetCallHierarchyName(parameters, out var objectName))
        {
            return Array.Empty<object>();
        }

        var objects = ObjectsByName();
        return AllDependencies()
            .Where(IsCallDependency)
            .Where(dependency => SameName(dependency.TargetObject, objectName))
            .Where(dependency => objects.ContainsKey(dependency.SourceObject))
            .GroupBy(dependency => dependency.SourceObject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                from = CallHierarchyItem(objects[group.Key]),
                fromRanges = group
                    .Select(dependency => Range(dependency.Line))
                    .Distinct()
                    .ToArray(),
            })
            .ToArray();
    }

    private object OutgoingCalls(JsonElement parameters)
    {
        if (!TryGetCallHierarchyName(parameters, out var objectName))
        {
            return Array.Empty<object>();
        }

        var objects = ObjectsByName();
        return AllDependencies()
            .Where(IsCallDependency)
            .Where(dependency => SameName(dependency.SourceObject, objectName))
            .Where(dependency => objects.ContainsKey(dependency.TargetObject))
            .GroupBy(dependency => dependency.TargetObject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                to = CallHierarchyItem(objects[group.Key]),
                fromRanges = group
                    .Select(dependency => Range(dependency.Line))
                    .Distinct()
                    .ToArray(),
            })
            .ToArray();
    }

    private bool TryGetCallHierarchyName(JsonElement parameters, out string objectName)
    {
        objectName = "";

        if (!parameters.TryGetProperty("item", out var item))
        {
            return false;
        }

        if (item.TryGetProperty("data", out var data)
            && data.TryGetProperty("name", out var dataName)
            && dataName.ValueKind == JsonValueKind.String)
        {
            objectName = dataName.GetString() ?? "";
        }
        else if (item.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String)
        {
            objectName = name.GetString() ?? "";
        }

        if (objectName.Length == 0)
        {
            return false;
        }

        objectName = Qualify(objectName.Replace("[", "").Replace("]", ""));

        return FindObject(objectName) is not null;
    }

    private Dictionary<string, DatabaseObject> ObjectsByName()
    {
        return AllObjects()
            .Select(item => item.Object)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private DatabaseObject? FindObject(string name)
    {
        return AllObjects()
            .Select(item => item.Object)
            .FirstOrDefault(item => SameName(item.Name, name));
    }

    private static bool IsCallDependency(Dependency dependency)
    {
        return dependency.Operation is "EXEC" or "CALL";
    }

    private static object CallHierarchyItem(DatabaseObject item)
    {
        return new
        {
            name = item.Name,
            kind = SymbolKind(item.Kind),
            detail = item.Kind,
            uri = LspUri.FromPath(item.SourceFile),
            range = Range(item.Line),
            selectionRange = Range(item.Line),
            data = new { name = item.Name },
        };
    }
}
