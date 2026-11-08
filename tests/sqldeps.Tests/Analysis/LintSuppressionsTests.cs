using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Tests.Analysis;

public sealed class LintSuppressionsTests
{
    [Fact]
    public void Filter_AppliesFileSuppressionOnlyToListedRules()
    {
        var findings = new[]
        {
            Finding("nolock", 2),
            Finding("select-star", 2),
            Finding("unknown-table", 2),
        };

        var filtered = LintSuppressions.Filter(
            findings,
            "-- sqldeps: disable-file=nolock, select-star\nSELECT * FROM dbo.Users;",
            startLine: 1
        ).ToArray();

        var finding = Assert.Single(filtered);
        Assert.Equal("unknown-table", finding.Rule);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal(2, finding.Line);
        Assert.Equal(1, finding.Column);
        Assert.Equal(1, finding.Length);
    }

    [Fact]
    public void HasRepositorySuppression_RecognizesListedRule()
    {
        var text = "# sqldeps: disable-repo=dynamic-sql, nolock\n";

        Assert.True(LintSuppressions.HasRepositorySuppression(text, "nolock"));
        Assert.False(LintSuppressions.HasRepositorySuppression(text, "select-star"));
    }

    [Fact]
    public void Filter_AppliesNextLineSuppressionToOnlyTheFollowingLine()
    {
        var findings = new[]
        {
            Finding("nolock", 2),
            Finding("nolock", 3),
        };

        var filtered = LintSuppressions.Filter(
            findings,
            "-- sqldeps: disable-next-line=nolock\nSELECT * FROM dbo.Users;\nSELECT * FROM dbo.Users;",
            startLine: 1
        ).ToArray();

        var finding = Assert.Single(filtered);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void Filter_UsesAbsoluteLinesWhenFullDocumentTextIsProvided()
    {
        var findings = new[] { Finding("nolock", 2), Finding("nolock", 3) };

        var filtered = LintSuppressions.Filter(
            findings,
            "SELECT * FROM dbo.Users;",
            startLine: 2,
            fullDocumentText: "-- sqldeps: disable-next-line=nolock\nSELECT * FROM dbo.Users;\nSELECT * FROM dbo.Users;"
        ).ToArray();

        var finding = Assert.Single(filtered);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void Filter_AppliesRepositorySuppressionFromSqldepsFile()
    {
        var directory = Directory.CreateTempSubdirectory("sqldeps-suppressions-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, ".sqldeps"),
                "sqldeps: disable-repo=dynamic-sql\n"
            );

            var filtered = LintSuppressions.Filter(
                new[] { Finding("dynamic-sql", 1), Finding("nolock", 1) },
                "SELECT 1;",
                startLine: 1,
                repositoryRoot: directory.FullName
            ).ToArray();

            var finding = Assert.Single(filtered);
            Assert.Equal("nolock", finding.Rule);
        }

        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static LintFinding Finding(string rule, int line)
    {
        return new LintFinding(
            rule,
            "warning",
            "dbo.Users",
            "test.sql",
            line,
            1,
            1,
            rule);
    }
}
