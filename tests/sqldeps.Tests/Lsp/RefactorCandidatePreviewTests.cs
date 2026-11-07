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
    public void AddDiagnosticContext_MarksDynamicDependencyImpactPartial()
    {
        const string text = "EXEC(N'SELECT * FROM ' + @tableName);";
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-dynamic-preview.sql");
        var databasePath = Path.Combine(Path.GetTempPath(), "sqldeps-dynamic-preview-index.sqlite");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 0),
                    new RefactorPosition(0, 1)),
                "-- preview\n")) with
        {
            Rule = "dynamic-sql-concatenation",
        };
        var analysis = new AnalysisResult(sourceFile, text);
        analysis.LintFindings.Add(new LintFinding(
            "dynamic-sql-concatenation",
            "warning",
            "(batch)",
            sourceFile,
            1,
            1,
            1,
            "Dynamic SQL uses a concatenated value."));
        analysis.Dependencies.Add(new Dependency(
            "(batch)",
            ObjectNames.DynamicSql,
            "EXEC_DYNAMIC",
            sourceFile,
            1,
            ViaDynamic: true));

        SqliteStore.Write(
            databasePath,
            objects: [],
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

        Assert.Equal("partial", enriched.AffectedDependencies.Status);
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(enriched));
    }

    [Fact]
    public void AddDiagnosticContext_MarksMissingDiagnosticObjectUnavailable()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-missing-object.sql");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 0),
                    new RefactorPosition(0, 1)),
                "id"));
        var analysis = new AnalysisResult(sourceFile, "SELECT * FROM dbo.Info;");

        var enriched = RefactorCandidatePreview.AddDiagnosticContext(candidate, analysis, queries: null);

        Assert.Equal("unavailable", enriched.AffectedDependencies.Status);
        Assert.Contains("source object", enriched.AffectedDependencies.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(enriched));
    }

    [Fact]
    public void AddDiagnosticContext_MarksMissingIndexUnavailable()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-missing-index.sql");
        var candidate = Candidate(
            LspUri.FromPath(sourceFile),
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(0, 0),
                    new RefactorPosition(0, 1)),
                "id"));
        var analysis = new AnalysisResult(sourceFile, "SELECT * FROM dbo.Info;");
        analysis.LintFindings.Add(new LintFinding(
            "select-star",
            "warning",
            "(batch)",
            sourceFile,
            1,
            1,
            1,
            "SELECT * can hide schema changes."));

        var enriched = RefactorCandidatePreview.AddDiagnosticContext(candidate, analysis, queries: null);

        Assert.Equal("unavailable", enriched.AffectedDependencies.Status);
        Assert.Contains("index", enriched.AffectedDependencies.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(enriched));
    }

    [Fact]
    public void Build_ProjectsAliasReplacementAgainstCurrentIndex()
    {
        const string text = "SELECT Info.id FROM dbo.Info i;";
        var root = Path.Combine(Path.GetTempPath(), "sqldeps-alias-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceFile = Path.Combine(root, "query.sql");
        var databasePath = Path.Combine(root, "index.sqlite");
        File.WriteAllText(sourceFile, text);

        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Info", "table", "query.sql", 1)],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [new TableColumn("dbo.Info", "id", "int", "query.sql", 1)],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile("query.sql", SqlFileReader.HashOf(File.ReadAllBytes(sourceFile)))],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, root),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: root);

        var queries = new IndexQueryService(new IndexDatabase(databasePath));
        var candidates = RefactorCandidatePreview.Build(
            queries.LoadAnalysisMetadata(),
            queries,
            sourceFile,
            text,
            ObjectNames.StandardDefaultSchema);

        var candidate = Assert.Single(candidates);
        Assert.Equal("table-name-after-alias", candidate.Rule);
        Assert.Equal("i.id", Assert.Single(candidate.Preview.Changes[LspUri.FromPath(sourceFile)]).NewText);
    }

    [Fact]
    public void Build_ReturnsParseValidCandidateWithCompleteImpact()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var root = Path.Combine(Path.GetTempPath(), "sqldeps-safe-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceFile = Path.Combine(root, "query.sql");
        var databasePath = Path.Combine(root, "index.sqlite");
        File.WriteAllText(sourceFile, text);

        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Info", "table", "query.sql", 1)],
            dependencies: [new Dependency("(batch)", "dbo.Info", "SELECT", "query.sql", 1)],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [
                new TableColumn("dbo.Info", "id", "int", "query.sql", 1),
                new TableColumn("dbo.Info", "title", "varchar", "query.sql", 1)],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile("query.sql", SqlFileReader.HashOf(File.ReadAllBytes(sourceFile)))],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, root),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: root);

        var queries = new IndexQueryService(new IndexDatabase(databasePath));
        var candidates = RefactorCandidatePreview.Build(
            queries.LoadAnalysisMetadata(),
            queries,
            sourceFile,
            text,
            ObjectNames.StandardDefaultSchema);

        var candidate = Assert.Single(candidates);

        Assert.Equal("select-star", candidate.Rule);
        Assert.Equal("complete", candidate.AffectedDependencies.Status);
        Assert.Equal("id, title", Assert.Single(candidate.Preview.Changes[LspUri.FromPath(sourceFile)]).NewText);
    }

    [Fact]
    public void Build_ReturnsNoCandidatesWhenIndexIsStale()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var root = Path.Combine(Path.GetTempPath(), "sqldeps-stale-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceFile = Path.Combine(root, "query.sql");
        var databasePath = Path.Combine(root, "index.sqlite");
        File.WriteAllText(sourceFile, text);

        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Info", "table", "query.sql", 1)],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [new TableColumn("dbo.Info", "id", "int", "query.sql", 1)],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile("query.sql", "old-hash")],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, root),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: root);

        var queries = new IndexQueryService(new IndexDatabase(databasePath));
        var candidates = RefactorCandidatePreview.Build(
            queries.LoadAnalysisMetadata(),
            queries,
            sourceFile,
            text,
            ObjectNames.StandardDefaultSchema);

        Assert.Empty(candidates);
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
    public void FindSourceObject_PrefersNearestColumnOnTheSameLine()
    {
        var candidate = Candidate(
            "file:///query.sql",
            new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(4, 19),
                    new RefactorPosition(4, 20)),
                "id"));
        var findings = new[]
        {
            new LintFinding("select-star", "warning", "dbo.Other", "query.sql", 5, 2, 1, "SELECT *"),
            new LintFinding("select-star", "warning", "dbo.Report", "query.sql", 5, 20, 1, "SELECT *"),
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

    [Fact]
    public void OrderCandidates_PrioritizesCompleteImpactCaseInsensitively()
    {
        RefactorCandidate incomplete = Candidate("file:///query.sql") with
        {
            Title = "Incomplete",
            AffectedDependencies = new RefactorDependencyImpact("partial", null, []),
        };
        RefactorCandidate complete = Candidate("file:///query.sql") with
        {
            Title = "Complete",
            AffectedDependencies = new RefactorDependencyImpact("COMPLETE", null, []),
        };

        RefactorCandidate[] ordered = RefactorCandidatePreview.OrderCandidates([incomplete, complete]);

        Assert.Equal("Complete", ordered[0].Title);
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

    [Fact]
    public void IsSafeCandidate_RejectsCandidatesThatWriteFiles()
    {
        RefactorCandidate candidate = Candidate("file:///query.sql") with { WritesFile = true };

        Assert.False(RefactorCandidatePreview.IsSafeCandidate(candidate));
    }

    [Fact]
    public void IsSafeCandidate_RejectsCandidatesWithoutApplicability()
    {
        RefactorCandidate candidate = Candidate("file:///query.sql") with { Applicability = null };

        Assert.False(RefactorCandidatePreview.IsSafeCandidate(candidate));
    }

    [Theory]
    [InlineData("select-star")]
    [InlineData("dynamic-sql-concatenation")]
    [InlineData("update-without-where")]
    [InlineData("delete-without-where")]
    [InlineData("missing-required-insert-column")]
    [InlineData("derived-query-column")]
    public void IsSafeCandidate_AppliesTheSameEvidenceMatrixToEveryCandidateRule(string rule)
    {
        RefactorCandidate safe = Candidate("file:///query.sql") with { Rule = rule };
        RefactorCandidate writesFile = safe with { WritesFile = true };
        RefactorCandidate missingApplicability = safe with { Applicability = null };
        RefactorCandidate partialImpact = safe with
        {
            AffectedDependencies = new RefactorDependencyImpact("partial", "dynamic", []),
        };

        Assert.True(RefactorCandidatePreview.IsSafeCandidate(safe));
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(writesFile));
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(missingApplicability));
        Assert.False(RefactorCandidatePreview.IsSafeCandidate(partialImpact));
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
            "The selected diagnostic must remain parse-valid.",
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
