using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Lsp;
using SqlDeps.Storage;

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
    public void TryApplyPreview_RejectsEditsForAnotherDocument()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-preview.sql");
        var candidate = Candidate(
            "file:///other.sql",
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 7),
                    new RefactorPosition(0, 8)),
                "id"));

        var applied = RefactorCandidatePreview.TryApplyPreview(
            candidate,
            sourceFile,
            text,
            out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApplyPreview_RejectsOverlappingEdits()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-preview.sql");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 7),
                    new RefactorPosition(0, 14)),
                "id"),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 10),
                    new RefactorPosition(0, 15)),
                "dbo.Other"));

        var applied = RefactorCandidatePreview.TryApplyPreview(
            candidate,
            sourceFile,
            text,
            out _);

        Assert.False(applied);
    }

    [Fact]
    public void AddDiagnosticContext_ResolvesImpactFromSelectedIndex()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-preview.sql");
        var databasePath = Path.Combine(Path.GetTempPath(), "sqldeps-preview-index.sqlite");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 7),
                    new RefactorPosition(0, 8)),
                "id"));
        var analysis = new AnalysisResult(sourceFile, text);
        analysis.LintFindings.Add(new LintFinding(
            "select-star",
            "warning",
            "(batch)",
            sourceFile,
            1,
            8,
            1,
            "SELECT * can hide schema changes."));
        analysis.Dependencies.Add(new Dependency(
            "(batch)",
            "dbo.Info",
            "SELECT",
            sourceFile,
            1));

        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Info", "table", "info.sql", 1)],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: Path.GetDirectoryName(sourceFile)!);

        var enriched = RefactorCandidatePreview.AddDiagnosticContext(
            candidate,
            analysis,
            new IndexQueryService(new IndexDatabase(databasePath)));

        Assert.Equal("(batch)", enriched.SourceObject);
        Assert.Equal("complete", enriched.AffectedDependencies.Status);
        Assert.Equal("dbo.Info", Assert.Single(enriched.AffectedDependencies.Items).ObjectName);
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

    [Fact]
    public void OrderCandidates_PrioritizesConfidenceAndUsesStableTies()
    {
        RefactorCandidate low = Candidate("file:///query.sql") with { Confidence = "low" };
        RefactorCandidate medium = Candidate("file:///query.sql") with
        {
            Title = "Medium",
            Rule = "b-rule",
            Confidence = "medium",
        };
        RefactorCandidate high = Candidate("file:///query.sql") with
        {
            Title = "High",
            Rule = "a-rule",
            Confidence = "high",
        };

        RefactorCandidate[] ordered = RefactorCandidatePreview.OrderCandidates([low, medium, high]);

        Assert.Equal("High", ordered[0].Title);
        Assert.Equal("Medium", ordered[1].Title);
        Assert.Equal("Preview", ordered[2].Title);
    }

    [Theory]
    [InlineData("partial", false)]
    [InlineData("unavailable", false)]
    [InlineData("complete", true)]
    public void IsSafeCandidate_RequiresCompleteDependencyImpact(string status, bool expected)
    {
        RefactorCandidate candidate = Candidate("file:///query.sql") with
        {
            AffectedDependencies = new RefactorDependencyImpact(status, null, []),
        };

        Assert.Equal(expected, RefactorCandidatePreview.IsSafeCandidate(candidate));
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
