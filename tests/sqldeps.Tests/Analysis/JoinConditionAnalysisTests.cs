using SqlDeps.Analysis;

namespace SqlDeps.Tests.Analysis;

public sealed class JoinConditionAnalysisTests
{
    [Fact]
    public void Analyze_DoesNotReportFalseConditionForOuterJoin()
    {
        const string sql = "SELECT users.id FROM dbo.Users AS users LEFT JOIN dbo.OtherUsers AS other ON 1 = 0;";

        var result = DocumentAnalysis.Analyze("joins.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "join-always-false-condition");
    }

    [Fact]
    public void Analyze_ReportsFalseUpdateInnerJoinAsDmlFinding()
    {
        const string sql = "UPDATE dbo.Users SET id = 1 FROM dbo.Users AS users INNER JOIN dbo.OtherUsers AS other ON 1 = 0;";

        var result = DocumentAnalysis.Analyze("joins.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "join-always-false-condition");
    }

    [Fact]
    public void Analyze_ReportsFalseDeleteInnerJoinAsDmlFinding()
    {
        const string sql = "DELETE users FROM dbo.Users AS users INNER JOIN dbo.OtherUsers AS other ON 1 = 0;";

        var result = DocumentAnalysis.Analyze("joins.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "join-always-false-condition");
    }
}
