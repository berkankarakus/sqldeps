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
    }

    [Fact]
    public void Parse_ReportsErrorsWithBatchLineOffsets()
    {
        const string text = "SELECT 1;\nGO\nSELECT FROM dbo.Info;";

        var result = new TsqlUpstreamParser().Parse(text);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Range.StartLine == 2);
    }
}
