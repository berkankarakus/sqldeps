using SqlDeps.Core;

namespace SqlDeps.Tests.Core;

public sealed class SqlQueryBuilderTests
{
    [Fact]
    public void BuildSelect_QuotesIdentifiersAndRemovesDuplicateColumns()
    {
        var preview = SqlQueryBuilder.BuildSelect(
            "dbo.Report]Rows",
            "table",
            ["Id", "id", "Name"]
        );

        Assert.Equal(["Id", "Name"], preview.Columns);
        Assert.Contains("FROM [dbo].[Report]]Rows] AS t", preview.Sql);
        Assert.Contains("t.[Id]", preview.Sql);
        Assert.Contains("t.[Name]", preview.Sql);
    }

    [Fact]
    public void BuildSelect_RejectsWhenNoIndexedColumnsRemain()
    {
        var exception = Assert.Throws<ArgumentException>(() => SqlQueryBuilder.BuildSelect(
            "dbo.Empty",
            "table",
            ["", "   "]
        ));

        Assert.Contains("No indexed columns found", exception.Message);
    }

    [Fact]
    public void BuildSelectWithJoin_QuotesBothSidesAndProducesParseablePreview()
    {
        var preview = SqlQueryBuilder.BuildSelectWithJoin(
            "dbo.Orders",
            "table",
            ["Id", "CustomerId"],
            "dbo.Customers",
            ["Id", "Name"],
            "CustomerId",
            "Id"
        );

        Assert.Equal("table + join", preview.ObjectKind);
        Assert.Contains("FROM [dbo].[Orders] AS t", preview.Sql);
        Assert.Contains("JOIN [dbo].[Customers] AS r ON r.[Id] = t.[CustomerId]", preview.Sql);
    }
}
