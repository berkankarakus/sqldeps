using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Tests.Mcp;

public sealed class RefactorImpactResolverTests
{
    [Fact]
    public void StaticDependencyTargets_DeduplicateCaseInsensitiveAndExcludeDynamicDependencies()
    {
        var dependencies = new[]
        {
            new Dependency("(batch)", "dbo.Info", "SELECT", "live.sql", 1),
            new Dependency("(batch)", "DBO.INFO", "SELECT", "live.sql", 2),
            new Dependency("(batch)", "dbo.DynamicTarget", "EXEC", "live.sql", 3, ViaDynamic: true),
        };

        var targets = RefactorImpactResolver.StaticDependencyTargets(dependencies);

        Assert.Equal(["dbo.Info"], targets);
    }

    [Fact]
    public void Resolve_ReportsGraphSummaryForDirectAndDownstreamDependencies()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"sqldeps-graph-aware-{Guid.NewGuid():N}.sqlite");
        Dependency downstream = new("dbo.ReadInfo", "dbo.Info", "SELECT", "read.sql", 2);
        SqliteStore.Write(
            databasePath,
            objects:
            [
                new DatabaseObject("dbo.Info", "table", "info.sql", 1),
                new DatabaseObject("dbo.ReadInfo", "procedure", "read.sql", 1),
            ],
            dependencies: [downstream],
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
            reachability: ReachabilityBuilder.Build([downstream], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: Path.GetTempPath());

        RefactorDependencyImpact impact = RefactorImpactResolver.Resolve(
            [new Dependency("dbo.ChangeInfo", "dbo.Info", "UPDATE", "change.sql", 1)],
            name => new IndexQueryService(new IndexDatabase(databasePath)).ObjectQuery("impact", name));

        Assert.NotNull(impact.Graph);
        Assert.Equal(1, impact.Graph!.DirectDependencyCount);
        Assert.Equal(1, impact.Graph.AffectedObjectCount);
        Assert.Equal(1, impact.Graph.MaximumDepth);
    }
}
