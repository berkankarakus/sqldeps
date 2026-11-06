using SqlDeps.Dialects.Tsql;

namespace SqlDeps.Tests.Dialects;

public sealed class TsqlUpstreamParserTests
{
    [Fact]
    public void Parse_AcceptsCommonTsqlQuery()
    {
        const string text = "SELECT t.col_f, t.rank FROM dbo.stat_clickrank_zoned AS t WHERE t.col_f = 1;";

        var result = new TsqlUpstreamParser().Parse(text);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var statement = Assert.Single(result.Statements);
        Assert.Equal("select", statement.Kind);
        Assert.Collection(
            statement.References,
            table => Assert.Equal("dbo.stat_clickrank_zoned", table.Name),
            firstColumn => Assert.Equal("t.col_f", firstColumn.Name),
            secondColumn => Assert.Equal("t.rank", secondColumn.Name)
        );
    }

    [Fact]
    public void Parse_ReportsErrorsWithBatchLineOffsets()
    {
        const string text = "SELECT 1;\nGO\nSELECT FROM dbo.Info;";

        var result = new TsqlUpstreamParser().Parse(text);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Range.StartLine == 2);
    }

    [Fact]
    public void Parse_MapsCreateTableTarget()
    {
        const string text = "CREATE TABLE dbo.Info (id int NOT NULL);";

        var result = new TsqlUpstreamParser().Parse(text);

        Assert.True(result.IsSuccess);
        var statement = Assert.Single(result.Statements);
        Assert.Equal("create-table", statement.Kind);
        Assert.Equal("dbo.Info", Assert.Single(statement.References).Name);
    }
}
