using SqlDeps.Domain;

namespace SqlDeps.Tests.Domain;

public sealed class DiagnosticRuleCatalogTests
{
    [Fact]
    public void TryGet_ReturnsEvidenceBackedExplanationForIndexedRule()
    {
        var found = DiagnosticRuleCatalog.TryGet("unindexed-where", out var explanation);

        Assert.True(found);
        Assert.Equal("unindexed-where", explanation.Rule);
        Assert.NotEmpty(explanation.Guidance);
        Assert.NotNull(explanation.References);
        Assert.Contains(explanation.References!, reference => reference.Url.Contains("learn.microsoft.com"));
    }

    [Fact]
    public void TryGet_IsCaseInsensitiveForKnownRules()
    {
        var found = DiagnosticRuleCatalog.TryGet("NOLOCK", out var explanation);

        Assert.True(found);
        Assert.Equal("nolock", explanation.Rule);
    }

    [Fact]
    public void TryGet_ReturnsGuidanceForPartialAnalysis()
    {
        var found = DiagnosticRuleCatalog.TryGet("partial-analysis", out var explanation);

        Assert.True(found);
        Assert.Equal("warning", explanation.DefaultSeverity);
        Assert.Contains("smaller batches", explanation.Guidance);
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownRuleWithoutInventingExplanation()
    {
        var found = DiagnosticRuleCatalog.TryGet("rule-that-does-not-exist", out _);

        Assert.False(found);
    }
}
