using SqlDeps.Domain;
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
    public void Analyze_ResolvesTableVariableColumnsThroughAnAlias()
    {
        var result = DocumentAnalysis.Analyze(
            "document.sql",
            "DECLARE @Rows TABLE (Id INT);\nSELECT rowsAlias.Id FROM @Rows AS rowsAlias;"
        );

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "unknown-column");
    }

    [Fact]
    public void Analyze_ResolvesDerivedTableColumnsFromSubqueryOutput()
    {
        var result = DocumentAnalysis.Analyze(
            "document.sql",
            "CREATE TABLE dbo.Users (Id INT);\nSELECT derived.Id FROM (SELECT Id FROM dbo.Users) AS derived;"
        );

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "unknown-column");
    }

    [Fact]
    public void Analyze_PreservesSameRuleFindingsAtDifferentColumns()
    {
        AnalysisResult result = DocumentAnalysis.Analyze(
            "document.sql",
            "SELECT * FROM dbo.Users WHERE name LIKE '%first%' AND title LIKE '%second%';"
        );

        LintFinding[] findings = result.LintFindings
            .Where(finding => finding.Rule == "leading-wildcard-like")
            .ToArray();

        Assert.Equal(2, findings.Length);
        Assert.NotEqual(findings[0].Column, findings[1].Column);
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
