using SqlDeps.Lsp;

using SqlDeps.Analysis;

namespace SqlDeps.Tests.Lsp;

public sealed class ColumnNameSuggestionTests
{
    [Fact]
    public void For_ReturnsUniqueCloseSuggestionIgnoringCharacterCase()
    {
        var suggestion = ColumnNameSuggestion.For(
            "custmer",
            ["CustomerId", "Customer"]
        );

        Assert.Equal("Customer", suggestion);
    }

    [Fact]
    public void For_ReturnsNullForShortNames()
    {
        var suggestion = ColumnNameSuggestion.For("id", ["Id", "Identifier"]);

        Assert.Null(suggestion);
    }

    [Fact]
    public void For_ReturnsNullWhenTwoCandidatesAreEquallyClose()
    {
        var suggestion = ColumnNameSuggestion.For("abcd", ["abce", "abcf"]);

        Assert.Null(suggestion);
    }

    [Fact]
    public void For_ReturnsNullWhenNoCandidateIsWithinTheDistanceLimit()
    {
        var suggestion = ColumnNameSuggestion.For("customer", ["order", "address"]);

        Assert.Null(suggestion);
    }
}
