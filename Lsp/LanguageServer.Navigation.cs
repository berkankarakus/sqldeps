using System.Text.Json;
using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object DocumentSymbols(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        AnalysisResult? analysis;
        lock (gate)
        {
            analysis = openDocuments.Get(uri);
        }

        return analysis?.Objects.Select(Symbol).ToArray() ?? Array.Empty<object>();
    }

    private object WorkspaceSymbols(JsonElement parameters)
    {
        var query = parameters.TryGetProperty("query", out var value) ? value.GetString() ?? "" : "";
        return AllObjects()
            .Where(item => item.Object.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSymbolResults)
            .Select(item => new
            {
                name = item.Object.Name,
                kind = SymbolKind(item.Object.Kind),
                location = Location(item.Object.SourceFile, item.Object.Line),
                containerName = item.Object.Kind,
            })
            .ToArray();
    }

    private object Definitions(JsonElement parameters)
    {
        if (TryGetColumnDefinition(parameters, out var column))
        {
            return new[] { Location(column.SourceFile, column.Line) };
        }

        var name = NameAt(parameters);
        return name is null
            ? Array.Empty<LspLocation>()
            : AllObjects().Where(item => SameName(item.Object.Name, name)).Select(item => Location(item.Object.SourceFile, item.Object.Line)).ToArray();
    }

    private object References(JsonElement parameters)
    {
        if (TryGetColumnDefinition(parameters, out var column))
        {
            var columnReferences = AllColumnUsage()
                .Where(usage => SameColumn(usage, column))
                .Select(usage => Location(usage.SourceFile, usage.Line))
                .Distinct()
                .ToList();
            var includeColumnDeclaration = parameters.TryGetProperty("context", out var columnContext)
                && columnContext.TryGetProperty("includeDeclaration", out var columnInclude)
                && columnInclude.GetBoolean();

            if (includeColumnDeclaration)
            {
                columnReferences.Add(Location(column.SourceFile, column.Line));
            }

            return columnReferences.Distinct().ToArray();
        }

        var name = NameAt(parameters);
        if (name is null)
        {
            return Array.Empty<LspLocation>();
        }

        var references = AllDependencies()
            .Where(dependency => SameName(dependency.TargetObject, name))
            .Select(dependency => Location(dependency.SourceFile, dependency.Line))
            .ToList();
        var includeDeclaration = parameters.TryGetProperty("context", out var context)
            && context.TryGetProperty("includeDeclaration", out var include)
            && include.GetBoolean();

        if (includeDeclaration)
        {
            references.AddRange(AllObjects().Where(item => SameName(item.Object.Name, name)).Select(item => Location(item.Object.SourceFile, item.Object.Line)));
        }

        return references;
    }
}
