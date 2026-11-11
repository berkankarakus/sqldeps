using System.Xml.Linq;

namespace SqlDeps.Tests.Architecture;

public sealed class DependencyDirectionTests
{
    private static readonly string[] LayerNames =
    [
        "Domain",
        "Analysis",
        "Storage",
        "Indexing",
        "Core",
        "Lsp",
        "Preview",
        "Mcp",
        "Cli",
    ];

    [Fact]
    public void SourceDependencyGraph_HasNoCyclesAndKeepsLowerLayersIndependent()
    {
        var graph = ReadGraph();

        Assert.Empty(graph["Domain"]);
        AssertAllowedDependencies(graph, "Analysis", "Domain");
        AssertAllowedDependencies(graph, "Storage", "Domain", "Analysis");
        AssertAllowedDependencies(graph, "Indexing", "Domain", "Analysis", "Storage");
        AssertAllowedDependencies(graph, "Core", "Domain", "Analysis");
        AssertNoCycles(graph);
    }

    [Fact]
    public void ProjectFiles_KeepReferencesAndSourceLinksWithinTheIntendedBoundary()
    {
        var core = ProjectFile("Core/SqlDeps.Core.csproj");
        var domain = ProjectFile("Domain/SqlDeps.Domain.csproj");
        var analysis = ProjectFile("Analysis/SqlDeps.Analysis.csproj");

        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(core));
        Assert.Contains("../Analysis/SqlDeps.Analysis.csproj", ProjectReferences(core));
        Assert.DoesNotContain("../Analysis/**/*.cs", CompileIncludes(core));
        Assert.Contains("../Indexing/**/*.cs", CompileIncludes(core));
        Assert.Contains("../Storage/**/*.cs", CompileIncludes(core));
        Assert.Contains("../Indexing/DependencyCollector*.cs", CompileIncludes(analysis));
        Assert.Contains("../Indexing/ConstantExpressionEvaluator*.cs", CompileIncludes(analysis));
        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(analysis));

        Assert.Empty(ProjectReferences(domain));
        Assert.DoesNotContain("../Indexing/**/*.cs", CompileIncludes(domain));
        Assert.DoesNotContain("../Storage/**/*.cs", CompileIncludes(domain));
    }

    [Fact]
    public void DocumentAnalysis_UsesTheCollectorBoundary()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Analysis/DocumentAnalysis.Parse.cs"));
        string collector = File.ReadAllText(Path.Combine(RepositoryRoot(), "Analysis/DocumentAnalysisCollector.cs"));

        Assert.Contains("DocumentAnalysisCollector.Append", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DependencyCollector", source, StringComparison.Ordinal);
        Assert.Contains("CreateContribution", collector, StringComparison.Ordinal);
        Assert.Contains("AppendDependencyContribution", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("collector.Objects", collector, StringComparison.Ordinal);
    }

    private static void AssertAllowedDependencies(
        IReadOnlyDictionary<string, HashSet<string>> graph,
        string layer,
        params string[] allowedLayers)
    {
        var allowed = allowedLayers.ToHashSet(StringComparer.Ordinal);
        var invalid = graph[layer]
            .Where(dependency => !allowed.Contains(dependency))
            .OrderBy(dependency => dependency, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            invalid.Length == 0,
            $"{layer} must only depend on {string.Join(", ", allowedLayers)}; found {string.Join(", ", invalid)}.");
    }

    private static XDocument ProjectFile(string relativePath)
    {
        return XDocument.Load(Path.Combine(RepositoryRoot(), relativePath));
    }

    private static IEnumerable<string> ProjectReferences(XDocument project)
    {
        return project
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .OfType<string>();
    }

    private static IEnumerable<string> CompileIncludes(XDocument project)
    {
        return project
            .Descendants("Compile")
            .Select(compile => (string?)compile.Attribute("Include"))
            .OfType<string>();
    }

    private static IReadOnlyDictionary<string, HashSet<string>> ReadGraph()
    {
        var graph = LayerNames.ToDictionary(
            layer => layer,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            var layer = LayerFor(file);

            if (layer is null)
            {
                continue;
            }

            foreach (var dependency in DependenciesIn(file))
            {
                if (!string.Equals(layer, dependency, StringComparison.Ordinal))
                {
                    graph[layer].Add(dependency);
                }
            }
        }

        return graph;
    }

    private static IEnumerable<string> SourceFiles()
    {
        string root = RepositoryRoot();

        foreach (var layer in LayerNames)
        {
            string directory = Path.Combine(root, layer);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string? LayerFor(string file)
    {
        string root = RepositoryRoot();
        string relativePath = Path.GetRelativePath(root, file);
        string layer = relativePath.Split(Path.DirectorySeparatorChar)[0];

        return LayerNames.Contains(layer, StringComparer.Ordinal) ? layer : null;
    }

    private static IEnumerable<string> DependenciesIn(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            string text = line.Trim();

            if (!text.StartsWith("using SqlDeps.", StringComparison.Ordinal)
                || !text.EndsWith(';'))
            {
                continue;
            }

            string dependency = text["using SqlDeps.".Length..^1];
            int separator = dependency.IndexOf('.', StringComparison.Ordinal);

            if (separator >= 0)
            {
                dependency = dependency[..separator];
            }

            if (LayerNames.Contains(dependency, StringComparer.Ordinal))
            {
                yield return dependency;
            }
        }
    }

    private static void AssertNoCycles(IReadOnlyDictionary<string, HashSet<string>> graph)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);

        foreach (var layer in graph.Keys)
        {
            Assert.False(
                HasCycle(layer, graph, visited, visiting),
                $"Dependency cycle found at {layer}.");
        }
    }

    private static bool HasCycle(
        string layer,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        ISet<string> visited,
        ISet<string> visiting)
    {
        if (visiting.Contains(layer))
        {
            return true;
        }

        if (visited.Contains(layer))
        {
            return false;
        }

        visiting.Add(layer);

        foreach (var dependency in graph[layer])
        {
            if (HasCycle(dependency, graph, visited, visiting))
            {
                return true;
            }
        }

        visiting.Remove(layer);
        visited.Add(layer);

        return false;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SqlDeps.csproj")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Repository root could not be found.");
        }

        return directory.FullName;
    }
}
