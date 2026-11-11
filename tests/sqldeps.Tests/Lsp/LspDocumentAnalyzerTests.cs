using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class LspDocumentAnalyzerTests
{
    [Fact]
    public void Analyze_UsesFullAnalysisWhenDocumentFitsLimit()
    {
        var result = LspDocumentAnalyzer.Analyze(
            "document.sql",
            "CREATE TABLE dbo.Known (Id INT);",
            40);

        Assert.False(result.IsPartial);
        Assert.Contains(result.Objects, item => item.Name == "dbo.Known");
    }

    [Fact]
    public void Analyze_UsesLimitedAnalysisWhenDocumentExceedsLimit()
    {
        var result = LspDocumentAnalyzer.Analyze(
            "document.sql",
            "CREATE TABLE dbo.Known (Id INT);\nGO\nCREATE TABLE dbo.Skipped (Id INT);",
            40);

        Assert.True(result.IsPartial);
        Assert.Contains("full-analysis limit", result.PartialReason);
        Assert.Contains(result.Objects, item => item.Name == "dbo.Known");
        Assert.DoesNotContain(result.Objects, item => item.Name == "dbo.Skipped");
    }
}
