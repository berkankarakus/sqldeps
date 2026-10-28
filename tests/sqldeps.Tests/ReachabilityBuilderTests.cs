using SqlDeps;

namespace SqlDeps.Tests;

public sealed class ReachabilityBuilderTests
{
    [Fact]
    public void Build_TransitiveCycleAndPseudoObjects_ReportsShortestPathsOnly()
    {
        var dependencies = new[]
        {
            new Dependency("dbo.A", "dbo.B", "EXEC", "a.sql", 1),
            new Dependency("dbo.B", "dbo.C", "SELECT", "b.sql", 1),
            new Dependency("dbo.C", "dbo.A", "UPDATE", "c.sql", 1),
            new Dependency("dbo.A", ObjectNames.DynamicSql, "EXEC_DYNAMIC", "a.sql", 2),
            new Dependency("(batch)", "dbo.Ignored", "SELECT", "loose.sql", 1),
        };

        var result = ReachabilityBuilder.Build(dependencies, maxDepth: 12);

        var fromA = result.Reach.Where(row => row.Source == "dbo.A").OrderBy(row => row.Depth).ToArray();
        Assert.Collection(
            fromA,
            first =>
            {
                Assert.Equal("dbo.B", first.Reached);
                Assert.Equal(1, first.Depth);
                Assert.Equal("dbo.A -EXEC-> dbo.B", first.Path);
            },
            second =>
            {
                Assert.Equal("dbo.C", second.Reached);
                Assert.Equal(2, second.Depth);
                Assert.Equal("dbo.A -EXEC-> dbo.B -SELECT-> dbo.C", second.Path);
            }
        );
        Assert.DoesNotContain(result.Reach, row => row.Reached == ObjectNames.DynamicSql || row.Reached == "dbo.Ignored");
        Assert.Contains(result.Cycles, cycle => cycle.ObjectName == "dbo.A" && cycle.Depth == 3 && cycle.Path.EndsWith("-UPDATE-> dbo.A"));
    }
}
