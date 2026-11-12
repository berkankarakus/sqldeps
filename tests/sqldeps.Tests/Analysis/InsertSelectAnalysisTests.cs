using SqlDeps.Analysis;

namespace SqlDeps.Tests.Analysis;

public sealed class InsertSelectAnalysisTests
{
    [Fact]
    public void Analyze_ReportsInsertSelectFalseWhereAsDmlFinding()
    {
        const string sql = "INSERT INTO dbo.Target (id) SELECT id FROM dbo.Source WHERE 1 = 0;";

        var result = DocumentAnalysis.Analyze("insert-select.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ReportsInsertSelectFalseHavingAsDmlFinding()
    {
        const string sql = "INSERT INTO dbo.Target (id) SELECT id FROM dbo.Source GROUP BY id HAVING 1 = 0;";

        var result = DocumentAnalysis.Analyze("insert-select.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ReportsInsertSelectFalseInnerJoinAsDmlFinding()
    {
        const string sql = "INSERT INTO dbo.Target (id) SELECT source.id FROM dbo.Source AS source INNER JOIN dbo.Other AS other ON 1 = 0;";

        var result = DocumentAnalysis.Analyze("insert-select.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "join-always-false-condition");
    }
}
