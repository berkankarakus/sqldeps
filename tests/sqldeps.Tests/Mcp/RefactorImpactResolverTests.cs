using SqlDeps;
using SqlDeps.Mcp;

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
}
