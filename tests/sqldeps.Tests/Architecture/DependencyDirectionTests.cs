using System.Xml.Linq;
using System.Text.RegularExpressions;
using SqlDeps.Cli;

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
        var indexing = ProjectFile("Indexing/SqlDeps.Indexing.csproj");
        var storage = ProjectFile("Storage/SqlDeps.Storage.csproj");
        var application = ProjectFile("SqlDeps.csproj");
        var tests = ProjectFile("tests/sqldeps.Tests/sqldeps.Tests.csproj");

        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(core));
        Assert.Contains("../Analysis/SqlDeps.Analysis.csproj", ProjectReferences(core));
        Assert.DoesNotContain("../Indexing/SqlDeps.Indexing.csproj", ProjectReferences(core));
        Assert.Contains("Storage/SqlDeps.Storage.csproj", ProjectReferences(application));
        Assert.Contains("Indexing/SqlDeps.Indexing.csproj", ProjectReferences(application));
        Assert.Contains("../../Storage/SqlDeps.Storage.csproj", ProjectReferences(tests));
        Assert.Contains("../../Indexing/SqlDeps.Indexing.csproj", ProjectReferences(tests));
        Assert.DoesNotContain("../Analysis/**/*.cs", CompileIncludes(core));
        Assert.DoesNotContain("../Indexing/**/*.cs", CompileIncludes(core));
        Assert.DoesNotContain("../Storage/**/*.cs", CompileIncludes(core));
        Assert.Contains("DependencyCollector/**/*.cs", CompileIncludes(analysis));
        Assert.DoesNotContain("../Indexing/DependencyCollector*.cs", CompileIncludes(analysis));
        Assert.DoesNotContain("../Indexing/ConstantExpressionEvaluator*.cs", CompileIncludes(analysis));
        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(analysis));

        Assert.Empty(ProjectReferences(domain));
        Assert.DoesNotContain("../Indexing/**/*.cs", CompileIncludes(domain));
        Assert.DoesNotContain("../Storage/**/*.cs", CompileIncludes(domain));
        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(storage));
        Assert.DoesNotContain("../Core/SqlDeps.Core.csproj", ProjectReferences(storage));
        Assert.DoesNotContain("../Indexing/**/*.cs", CompileIncludes(storage));
        Assert.Contains("../Domain/SqlDeps.Domain.csproj", ProjectReferences(indexing));
        Assert.Contains("../Analysis/SqlDeps.Analysis.csproj", ProjectReferences(indexing));
        Assert.DoesNotContain("../Core/SqlDeps.Core.csproj", ProjectReferences(indexing));
        Assert.DoesNotContain("../Storage/SqlDeps.Storage.csproj", ProjectReferences(indexing));
    }

    [Fact]
    public void AssemblyFriendDeclarations_StayWithKnownHostOrTestConsumers()
    {
        AssertFriendAssemblies(
            "Analysis/Properties/AssemblyInfo.cs",
            "sqldeps",
            "sqldeps.Tests");
        AssertFriendAssemblies(
            "Core/Properties/AssemblyInfo.cs",
            "sqldeps",
            "sqldeps.Tests");
        AssertFriendAssemblies(
            "Indexing/Properties/AssemblyInfo.cs",
            "sqldeps.Tests");
        AssertFriendAssemblies(
            "Storage/Properties/AssemblyInfo.cs",
            "sqldeps.Tests");
    }

    [Fact]
    public void DocumentAnalysis_UsesTheCollectorBoundary()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Analysis/DocumentAnalysis.Parse.cs"));
        string collector = File.ReadAllText(Path.Combine(RepositoryRoot(), "Analysis/DocumentAnalysisCollector.cs"));

        Assert.Contains("DocumentAnalysisCollector.Append", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DependencyCollector", source, StringComparison.Ordinal);
        Assert.Contains("AppendDependencyContribution", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("collector.Objects", collector, StringComparison.Ordinal);

        string indexer = File.ReadAllText(Path.Combine(RepositoryRoot(), "Indexing/SqlFolderIndexer.Parsing.cs"));
        string adapter = File.ReadAllText(Path.Combine(RepositoryRoot(), "Analysis/DependencyContributionCollector.cs"));
        Assert.Contains("CreateContribution", adapter, StringComparison.Ordinal);
        Assert.Contains("DependencyContributionCollector.Collect", collector, StringComparison.Ordinal);
        Assert.Contains("DependencyCollectionAnalyzer.Collect", indexer, StringComparison.Ordinal);
        Assert.DoesNotContain("DependencyContributionCollector.Collect", indexer, StringComparison.Ordinal);
        Assert.DoesNotContain("new DependencyCollector", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("new DependencyCollector", indexer, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisSources_UseTheAnalysisNamespace()
    {
        string root = Path.Combine(RepositoryRoot(), "Analysis");
        string[] rootNamespaceFiles = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file
                .Split(Path.DirectorySeparatorChar)
                .Contains("obj", StringComparer.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("namespace SqlDeps;", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();

        Assert.Empty(rootNamespaceFiles);
    }

    [Fact]
    public void SharedIndexCacheContracts_LiveBelowIndexingAndStorage()
    {
        string domainModels = File.ReadAllText(Path.Combine(RepositoryRoot(), "Domain/IndexCacheModels.cs"));
        string indexCache = File.ReadAllText(Path.Combine(RepositoryRoot(), "Storage/IndexCache.cs"));
        string batchCache = File.ReadAllText(Path.Combine(RepositoryRoot(), "Indexing/BatchContributionCache.cs"));

        Assert.Contains("record CachedFile", domainModels, StringComparison.Ordinal);
        Assert.Contains("record CachedBatch", domainModels, StringComparison.Ordinal);
        Assert.DoesNotContain("record CachedFile", indexCache, StringComparison.Ordinal);
        Assert.DoesNotContain("record CachedBatch", batchCache, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexCacheImplementation_LivesInStorageLayer()
    {
        string storageCache = Path.Combine(RepositoryRoot(), "Storage/IndexCache.cs");
        string storageConnection = Path.Combine(RepositoryRoot(), "Storage/IndexCache.Connection.cs");
        string storageHydration = Path.Combine(RepositoryRoot(), "Storage/IndexCache.Hydration.cs");
        string indexingCache = Path.Combine(RepositoryRoot(), "Indexing/IndexCache.cs");

        Assert.True(File.Exists(storageCache));
        Assert.True(File.Exists(storageConnection));
        Assert.True(File.Exists(storageHydration));
        Assert.False(File.Exists(indexingCache));
    }

    [Fact]
    public void SharedFileInfrastructure_LivesBelowCore()
    {
        string domainReader = Path.Combine(RepositoryRoot(), "Domain/SqlFileReader.cs");
        string domainCatalog = Path.Combine(RepositoryRoot(), "Domain/SqlFileCatalog.cs");
        string coreReader = Path.Combine(RepositoryRoot(), "Core/SqlFileReader.cs");
        string coreCatalog = Path.Combine(RepositoryRoot(), "Core/SqlFileCatalog.cs");

        Assert.True(File.Exists(domainReader));
        Assert.True(File.Exists(domainCatalog));
        Assert.False(File.Exists(coreReader));
        Assert.False(File.Exists(coreCatalog));
    }

    [Fact]
    public void IndexerFileDiscoveryRemainsAnInternalDomainCatalogAdapter()
    {
        string indexer = File.ReadAllText(Path.Combine(RepositoryRoot(), "Indexing/SqlFolderIndexer.cs"));

        Assert.Contains("internal static IEnumerable<string> FindSqlFiles", indexer, StringComparison.Ordinal);
        Assert.Contains("return SqlFileCatalog.FindSqlFiles(rootFolder, includePatterns, excludePatterns);", indexer, StringComparison.Ordinal);
    }

    [Fact]
    public void CliHostTypesRemainInternalToTheExecutable()
    {
        Assert.False(typeof(CommandLine).IsPublic);
        Assert.False(typeof(ConsoleOutput).IsPublic);
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

    private static void AssertFriendAssemblies(
        string relativePath,
        params string[] expectedAssemblies)
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
        string[] actualAssemblies = Regex
            .Matches(source, "InternalsVisibleTo\\(\"([^\"]+)\"\\)")
            .Select(match => match.Groups[1].Value)
            .OrderBy(assembly => assembly, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedAssemblies.OrderBy(assembly => assembly, StringComparer.Ordinal),
            actualAssemblies);
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
