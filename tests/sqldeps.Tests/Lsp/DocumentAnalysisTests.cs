using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class DocumentAnalysisTests
{
    [Fact]
    public void Analyze_CollectsObjectsColumnsDependenciesAndParsedBatches()
    {
        var result = DocumentAnalysis.Analyze(
            "document.sql",
            "CREATE TABLE dbo.Users (Id INT);\nSELECT Id FROM dbo.Users;"
        );

        Assert.Single(result.ParsedBatches);
        Assert.Contains(result.Objects, item => item.Name == "dbo.Users" && item.Kind == "table");
        Assert.Contains(result.TableColumns, item => item.TableName == "dbo.Users" && item.ColumnName == "Id");
        Assert.Contains(result.Dependencies, item => item.TargetObject == "dbo.Users" && item.Operation == "SELECT");
    }

    [Fact]
    public void Analyze_CollectsCteOutputColumnsForLocalResolution()
    {
        var result = DocumentAnalysis.Analyze(
            "document.sql",
            "WITH ActiveUsers AS (SELECT 1 AS Id) SELECT Id FROM ActiveUsers;"
        );

        var localColumn = Assert.Single(result.LocalColumns, column =>
            column.Alias == "ActiveUsers" && column.Name == "Id");

        Assert.Equal("CTE", localColumn.Kind);
    }

    [Fact]
    public void AnalyzeBatchAtLine_SelectsOnlyTheBatchContainingTheRequestedLine()
    {
        var result = DocumentAnalysis.AnalyzeBatchAtLine(
            "document.sql",
            "SELECT 1;\nGO\nCREATE TABLE dbo.Users (Id INT);",
            2
        );

        Assert.Equal("CREATE TABLE dbo.Users (Id INT);\n", result.Text);
        Assert.Contains(result.Objects, item => item.Name == "dbo.Users");
    }

    [Fact]
    public void ParseProblemCountForBatch_ReportsOnlyTheSelectedMalformedBatch()
    {
        var text = "SELECT 1;\nGO\nSELECT FROM;";

        var validBatchProblems = DocumentAnalysis.ParseProblemCountForBatch("document.sql", text, 0);
        var malformedBatchProblems = DocumentAnalysis.ParseProblemCountForBatch("document.sql", text, 2);

        Assert.Equal(0, validBatchProblems);
        Assert.True(malformedBatchProblems > 0);
    }
}
