using System.Reflection;
using System.Text.Json;
using SqlDeps.Core;

namespace SqlDeps.Tests.Core;

public sealed class CoreApiCompatibilityTests
{
    [Fact]
    public void PublicContractsRemainHostedByCoreAssembly()
    {
        const string expectedAssemblyName = "SqlDeps.Core";

        Assert.Equal(expectedAssemblyName, typeof(IndexQueryService).Assembly.GetName().Name);
        Assert.Equal(expectedAssemblyName, typeof(RefactorCandidateProjector).Assembly.GetName().Name);
        Assert.Equal(expectedAssemblyName, typeof(RefactorImpactResolver).Assembly.GetName().Name);
        Assert.Equal(expectedAssemblyName, typeof(LintSuppressions).Assembly.GetName().Name);
    }

    [Fact]
    public void CoreNamespacePublicSurfaceRemainsStable()
    {
        Assembly coreAssembly = typeof(IndexQueryService).Assembly;
        string[] expectedTypes =
        [
            "SqlDeps.Core.IndexAnalysisMetadata",
            "SqlDeps.Core.IndexQueryService",
            "SqlDeps.Core.QueryPreview",
            "SqlDeps.Core.RefactorCandidate",
            "SqlDeps.Core.RefactorCandidateProjector",
            "SqlDeps.Core.RefactorDependencyImpact",
            "SqlDeps.Core.RefactorDependencyItem",
            "SqlDeps.Core.RefactorImpactResolver",
            "SqlDeps.Core.RefactorImpactRow",
            "SqlDeps.Core.RefactorPosition",
            "SqlDeps.Core.RefactorRange",
            "SqlDeps.Core.RefactorTextEdit",
            "SqlDeps.Core.RefactorWorkspaceEdit",
            "SqlDeps.Core.SqlFormatResult",
            "SqlDeps.Core.SqlFormatter",
            "SqlDeps.Core.SqlQueryBuilder",
            "SqlDeps.Core.SqlTestSkeleton",
            "SqlDeps.Core.SqlTestSkeletonBuilder",
        ];

        string[] actualTypes = coreAssembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, "SqlDeps.Core", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedTypes.OrderBy(type => type, StringComparer.Ordinal), actualTypes);
    }

    [Fact]
    public void ParserAndSqliteHelpersRemainInternal()
    {
        Assembly coreAssembly = typeof(IndexQueryService).Assembly;
        string[] internalHelpers =
        [
            "SqlDeps.DynamicSqlText",
            "SqlDeps.SqliteMetadataMapper",
            "SqlDeps.SqliteSchema",
        ];

        foreach (string helperName in internalHelpers)
        {
            Type helper = coreAssembly.GetType(helperName, throwOnError: true)!;

            Assert.False(helper.IsPublic, helperName);
        }
    }

    [Fact]
    public void RefactorProjectorRemainsUsableThroughPublicCoreContract()
    {
        using var document = JsonDocument.Parse("""
            [{
              "title": "Expand SELECT *",
              "data": {"writesFile": false, "confidence": "high", "rule": "select-star"},
              "edit": {"changes": {"file:///query.sql": [{
                "range": {"start": {"line": 0, "character": 7}, "end": {"line": 0, "character": 8}},
                "newText": "id"
              }]}}
            }]
            """);

        var candidates = RefactorCandidateProjector.Project(document.RootElement.EnumerateArray());

        var candidate = Assert.Single(candidates);
        Assert.Equal("select-star", candidate.Rule);
        Assert.False(candidate.WritesFile);
    }
}
