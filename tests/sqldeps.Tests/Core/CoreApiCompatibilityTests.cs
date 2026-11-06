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
