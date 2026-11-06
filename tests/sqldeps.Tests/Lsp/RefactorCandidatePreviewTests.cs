using SqlDeps.Core;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class RefactorCandidatePreviewTests
{
    [Fact]
    public void FindSourceObject_MatchesRuleAndNearestEditLine()
    {
        var candidate = new RefactorCandidate(
            "Expand SELECT *",
            "quickfix",
            "select-star",
            "high",
            "Indexed columns are complete.",
            [],
            new RefactorDependencyImpact("complete", null, []),
            new RefactorWorkspaceEdit(
                new Dictionary<string, IReadOnlyList<RefactorTextEdit>>
                {
                    ["file:///query.sql"] =
                    [
                        new RefactorTextEdit(
                            new RefactorRange(
                                new RefactorPosition(4, 0),
                                new RefactorPosition(4, 1)),
                            "id")
                    ],
                }),
            WritesFile: false);
        var findings = new[]
        {
            new LintFinding("select-star", "warning", "dbo.Report", "query.sql", 5, 1, 1, "SELECT *"),
            new LintFinding("select-star", "warning", "dbo.Other", "query.sql", 20, 1, 1, "SELECT *"),
        };

        var sourceObject = RefactorCandidatePreview.FindSourceObject(candidate, findings);

        Assert.Equal("dbo.Report", sourceObject);
    }

    [Fact]
    public void FindSourceObject_ReturnsNullWhenRuleHasNoDiagnosticObject()
    {
        var candidate = new RefactorCandidate(
            "Expand SELECT *",
            "quickfix",
            "select-star",
            "high",
            null,
            [],
            new RefactorDependencyImpact("complete", null, []),
            new RefactorWorkspaceEdit(
                new Dictionary<string, IReadOnlyList<RefactorTextEdit>>
                {
                    ["file:///query.sql"] =
                    [
                        new RefactorTextEdit(
                            new RefactorRange(
                                new RefactorPosition(0, 0),
                                new RefactorPosition(0, 1)),
                            "id")
                    ],
                }),
            WritesFile: false);

        var sourceObject = RefactorCandidatePreview.FindSourceObject(
            candidate,
            [new LintFinding("nolock", "warning", "dbo.Report", "query.sql", 1, 1, 1, "NOLOCK")]);

        Assert.Null(sourceObject);
    }
}
