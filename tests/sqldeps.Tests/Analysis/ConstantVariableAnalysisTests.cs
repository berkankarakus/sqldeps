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
    public void Analyze_ResolvesCaseAssignmentWithSameLiteralBranches()
    {
        const string sql = "DECLARE @include int; SET @include = CASE WHEN @flag = 1 THEN 1 ELSE 1 END; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesKnownTrueCaseBranch()
    {
        const string sql = "DECLARE @include int; SET @include = CASE WHEN 1 = 1 THEN 1 ELSE 0 END; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesKnownFalseCaseBranchWithElse()
    {
        const string sql = "DECLARE @include int; SET @include = CASE WHEN 1 = 0 THEN 0 ELSE 1 END; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveCaseAssignmentWithoutElse()
    {
        const string sql = "DECLARE @include int; SET @include = CASE WHEN 1 = 1 THEN 1 END; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_UsesLiteralVariableInNumericExpression()
    {
        const string sql = "DECLARE @limit int = 1; SELECT id FROM dbo.Users WHERE @limit + 1 = 2;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_UsesLiteralVariableInTemporalExpression()
    {
        const string sql = "DECLARE @since datetime = '2025-01-01'; SELECT id FROM dbo.Users WHERE @since < '2026-01-01';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_PreservesTemporalComparisonSemanticsForEquivalentFormats()
    {
        const string sql = "DECLARE @since datetime = '2025-01-01'; SELECT id FROM dbo.Users WHERE @since = '20250101';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_LeavesAmbiguousTemporalLiteralUnknown()
    {
        const string sql = "DECLARE @since datetime = 'not-a-date'; SELECT id FROM dbo.Users WHERE @since < '2026-01-01';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesUnknownOrTrueCondition()
    {
        const string sql = "SELECT id FROM dbo.Users WHERE @flag = 1 OR 1 = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesUnknownAndFalseCondition()
    {
        const string sql = "UPDATE dbo.Users SET active = 1 WHERE @flag = 1 AND 1 = 0;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "dml-always-false-filter");
    }

    [Fact]
    public void Analyze_ResolvesNullVariableIsNullCondition()
    {
        const string sql = "DECLARE @value int = NULL; SELECT id FROM dbo.Users WHERE @value IS NULL;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesNullVariableIsNotNullCondition()
    {
        const string sql = "DECLARE @value int = NULL; SELECT id FROM dbo.Users WHERE @value IS NOT NULL;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableBetweenCondition()
    {
        const string sql = "DECLARE @value int = 5; SELECT id FROM dbo.Users WHERE @value BETWEEN 1 AND 10;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableTemporalBetweenCondition()
    {
        const string sql = "DECLARE @since datetime = '2025-01-01'; SELECT id FROM dbo.Users WHERE @since BETWEEN '2024-01-01' AND '2026-01-01';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableInCondition()
    {
        const string sql = "DECLARE @value int = 2; SELECT id FROM dbo.Users WHERE @value NOT IN (1, 2);";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableStringComparison()
    {
        const string sql = "DECLARE @status nvarchar(20) = N'active'; SELECT id FROM dbo.Users WHERE @status = N'archived';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ResolvesMatchingVariableStringComparison()
    {
        const string sql = "DECLARE @status nvarchar(20) = N'active'; SELECT id FROM dbo.Users WHERE @status = N'active';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableStringInCondition()
    {
        const string sql = "DECLARE @status nvarchar(20) = N'active'; SELECT id FROM dbo.Users WHERE @status IN (N'archived', N'deleted');";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_ResolvesVariableStringNotInCondition()
    {
        const string sql = "DECLARE @status nvarchar(20) = N'active'; SELECT id FROM dbo.Users WHERE @status NOT IN (N'archived', N'deleted');";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_PreservesTemporalSemanticsInVariableInCondition()
    {
        const string sql = "DECLARE @since datetime = '2025-01-01'; SELECT id FROM dbo.Users WHERE @since IN ('20250101', '2026-01-01');";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotTreatDateLikeStringVariableAsTemporal()
    {
        const string sql = "DECLARE @status nvarchar(20) = '2025-01-01'; SELECT id FROM dbo.Users WHERE @status = '20250101';";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-always-false-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveReassignedVariable()
    {
        const string sql = "DECLARE @include int = 0; SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesDefinitelyTrueConditionalAssignment()
    {
        const string sql = "DECLARE @include int; IF 1 = 1 SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesDefinitelyFalseConditionalElseAssignment()
    {
        const string sql = "DECLARE @include int; IF 1 = 0 SET @include = 0; ELSE SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveDefinitelyFalseConditionalWithoutElse()
    {
        const string sql = "DECLARE @include int; IF 1 = 0 SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesBranchAssignmentWhenAllPathsHaveTheSameLiteral()
    {
        const string sql = "DECLARE @include int; IF @flag = 1 SET @include = 1; ELSE SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveBranchAssignmentWhenPathsHaveDifferentLiterals()
    {
        const string sql = "DECLARE @include int; IF @flag = 1 SET @include = 1; ELSE SET @include = 0; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveLoopAssignmentBecauseTheLoopMayNotRun()
    {
        const string sql = "DECLARE @include int; WHILE @flag = 1 SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_DoesNotResolveAssignmentFromDefinitelyFalseLoop()
    {
        const string sql = "DECLARE @include int; WHILE 1 = 0 SET @include = 1; SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.DoesNotContain(result.LintFindings, finding => finding.Rule is "select-always-false-filter" or "select-ineffective-filter");
    }

    [Fact]
    public void Analyze_ResolvesTryCatchAssignmentWhenBothPathsHaveTheSameLiteral()
    {
        const string sql = "DECLARE @include int; BEGIN TRY SET @include = 1; END TRY BEGIN CATCH SET @include = 1; END CATCH SELECT id FROM dbo.Users WHERE @include = 1;";

        var result = DocumentAnalysis.Analyze("variables.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == "select-ineffective-filter");
    }
}
