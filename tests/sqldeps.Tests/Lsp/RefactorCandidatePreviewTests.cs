using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class RefactorCandidatePreviewTests
{
    [Fact]
    public void TryApplyPreview_UsesOriginalRangesForMultipleEdits()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-preview.sql");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 7),
                    new RefactorPosition(0, 8)),
                "id"),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 14),
                    new RefactorPosition(0, 22)),
                "dbo.Other"));

        var applied = RefactorCandidatePreview.TryApplyPreview(
            candidate,
            sourceFile,
            text,
            out var previewText);

        Assert.True(applied);
        Assert.Equal("SELECT id FROM dbo.Other;", previewText);
    }

    [Fact]
    public void IsParseValidPreview_RejectsPreviewThatIntroducesParseErrors()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-preview.sql");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 7),
                    new RefactorPosition(0, 8)),
                ")"));

        var isValid = RefactorCandidatePreview.IsParseValidPreview(
            candidate,
            sourceFile,
            text,
            ObjectNames.StandardDefaultSchema);

        Assert.False(isValid);
    }

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

    private static RefactorCandidate Candidate(
        string uri,
        params RefactorTextEdit[] edits)
    {
        return new RefactorCandidate(
            "Preview",
            "quickfix",
            "select-star",
            "high",
            null,
            [],
            new RefactorDependencyImpact("complete", null, []),
            new RefactorWorkspaceEdit(
                new Dictionary<string, IReadOnlyList<RefactorTextEdit>>
                {
                    [uri] = edits,
                }),
            WritesFile: false);
    }
}
