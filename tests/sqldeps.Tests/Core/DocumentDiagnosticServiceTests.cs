using SqlDeps.Core;
using SqlDeps.Domain;

namespace SqlDeps.Tests.Core;

public sealed class DocumentDiagnosticServiceTests
{
    [Fact]
    public void Analyze_ReturnsParseProblemsWithoutChangingIndexedMetadata()
    {
        IndexAnalysisMetadata indexed = EmptyMetadata();

        var result = DocumentDiagnosticService.Analyze(
            "document.sql",
            "SELECT FROM;",
            indexed,
            "dbo");

        Assert.NotEmpty(result.Problems);
        Assert.Empty(indexed.Objects);
        Assert.Empty(indexed.Catalog.TableColumns);
    }

    [Fact]
    public void Analyze_ReturnsNoFindingsForAValidConstantQuery()
    {
        var result = DocumentDiagnosticService.Analyze(
            "document.sql",
            "SELECT 1;",
            EmptyMetadata(),
            "dbo");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Problems);
    }

    private static IndexAnalysisMetadata EmptyMetadata()
    {
        return new IndexAnalysisMetadata(
            new MetadataCatalog([], [], [], [], []),
            [],
            []);
    }
}
