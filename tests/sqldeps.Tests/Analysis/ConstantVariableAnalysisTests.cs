using SqlDeps.Analysis;

namespace SqlDeps.Tests.Analysis;

public sealed class ConstantVariableAnalysisTests
{
    [Fact]
    public void Analyze_UsesLiteralVariableInSelectFilter()
    {
        const string sql = "DECLARE @include int = 0; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_UsesSingleLiteralSetAssignment()
    {
        const string sql = "DECLARE @include int; SET @include = 1; UPDATE dbo.Users SET active = 1 WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveReassignedVariable()
    {
        const string sql = "DECLARE @include int = 0; SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveConditionalAssignment()
    {
        const string sql = "DECLARE @include int; IF 1 = 1 SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }
}
