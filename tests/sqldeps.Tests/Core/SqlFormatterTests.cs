using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Core;

namespace SqlDeps.Tests.Core;

public sealed class SqlFormatterTests
{
    [Fact]
    public void Format_PreservesCommentsStringsAndBatchBoundaries()
    {
        const string text = "-- keep this comment\nSELECT 'a  b' AS value FROM dbo.Info;\nGO\nSELECT 1;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("keep this comment", result.Text, StringComparison.Ordinal);
        Assert.Contains("'a  b'", result.Text, StringComparison.Ordinal);
        Assert.Contains("\nGO\n", result.Text, StringComparison.Ordinal);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Format_RejectsInvalidSqlWithoutChangingPreviewText()
    {
        const string text = "SELECT FROM;";

        var result = SqlFormatter.Format(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(text, result.Text);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Format_RejectsGoRepeatCountsUntilTheyCanRoundTripSafely()
    {
        const string text = "SELECT 1;\nGO 2\n";

        var result = SqlFormatter.Format(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(text, result.Text);
        Assert.Contains("repeat counts", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReturnsParseableOutput()
    {
        const string text = "select * from dbo.Info where id=1;";

        var result = SqlFormatter.Format(text);
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(result.Text);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        Assert.True(result.IsSuccess);
        Assert.NotNull(fragment);
        Assert.Empty(errors);
    }
}
