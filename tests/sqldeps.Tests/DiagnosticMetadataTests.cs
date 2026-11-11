using SqlDeps.Domain;

namespace SqlDeps.Tests;

public sealed class DiagnosticMetadataTests
{
    [Fact]
    public void ParseErrorsAreLowConfidenceWithTheParserCompatibilityReason()
    {
        var metadata = DiagnosticMetadataMapper.For("live-document", "parse-error", "error");

        Assert.Equal("low", metadata.Confidence);
        Assert.Equal("scriptdom-tsql160-parser", metadata.ConfidenceReason);
        Assert.False(metadata.IsStale);
    }

    [Fact]
    public void UnqualifiedUnknownObjectsExposeTheMissingCatalogReason()
    {
        var metadata = DiagnosticMetadataMapper.For("fresh-index", "unknown-table", "info");

        Assert.Equal("low", metadata.Confidence);
        Assert.Equal("source-index-no-catalog", metadata.ConfidenceReason);
    }

    [Fact]
    public void AlwaysFalseSelectFiltersAreLowConfidenceReviewSuggestions()
    {
        var metadata = DiagnosticMetadataMapper.For("live-document", "select-always-false-filter", "info");

        Assert.Equal("low", metadata.Confidence);
        Assert.False(metadata.IsStale);
    }

    [Fact]
    public void AlwaysTrueSelectFiltersAreLowConfidenceReviewSuggestions()
    {
        var metadata = DiagnosticMetadataMapper.For("live-document", "select-ineffective-filter", "info");

        Assert.Equal("low", metadata.Confidence);
        Assert.False(metadata.IsStale);
    }

    [Fact]
    public void UnreachableBranchesAreLowConfidenceReviewSuggestions()
    {
        var metadata = DiagnosticMetadataMapper.For("live-document", "unreachable-branch", "info");

        Assert.Equal("low", metadata.Confidence);
        Assert.False(metadata.IsStale);
    }

    [Fact]
    public void StaleMetadataKeepsFreshnessAsTheDominantReason()
    {
        var metadata = DiagnosticMetadataMapper.For("stale-index", "unknown-table", "warning");

        Assert.Equal("low", metadata.Confidence);
        Assert.True(metadata.IsStale);
        Assert.Equal("stale-index", metadata.ConfidenceReason);
    }
}
