using SqlDeps.Domain;

namespace SqlDeps.Tests.Domain;

public sealed class DiagnosticProfileTests
{
    [Theory]
    [InlineData("dynamic-sql")]
    [InlineData("dynamic-sql-concatenation")]
    [InlineData("update-without-where")]
    [InlineData("unbound-identifier")]
    public void SecurityProfile_IncludesRulesThatCanExposeUnsafeSql(string rule)
    {
        DiagnosticProfile profile = DiagnosticProfileCatalog.Get("security");

        Assert.True(profile.Includes(rule));
    }

    [Theory]
    [InlineData("select-star")]
    [InlineData("unindexed-where")]
    [InlineData("order-by-ordinal")]
    [InlineData("select-always-false-filter")]
    [InlineData("select-ineffective-filter")]
    public void SecurityProfile_ExcludesPerformanceOnlyRules(string rule)
    {
        DiagnosticProfile profile = DiagnosticProfileCatalog.Get("security");

        Assert.False(profile.Includes(rule));
    }

    [Fact]
    public void UnknownProfile_ThrowsInsteadOfFallingBackToDefault()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => DiagnosticProfileCatalog.Get("unknown-profile"));

        Assert.Contains("Available profiles", exception.Message);
    }

    [Fact]
    public void DefaultProfile_IncludesRulesOutsideSpecializedProfiles()
    {
        DiagnosticProfile profile = DiagnosticProfileCatalog.Get(null);

        Assert.True(profile.Includes("select-star"));
        Assert.True(profile.Includes("dynamic-sql"));
    }
}
