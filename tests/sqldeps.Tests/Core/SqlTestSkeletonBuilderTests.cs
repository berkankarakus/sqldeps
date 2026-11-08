using SqlDeps.Core;
using SqlDeps.Domain;

namespace SqlDeps.Tests.Core;

public sealed class SqlTestSkeletonBuilderTests
{
    [Fact]
    public void Build_OrdersParametersAndQuotesProcedureName()
    {
        var parameters = new[]
        {
            new ObjectParameter("dbo.Run Report", 2, "@name", "nvarchar(50)", null, false, "report.sql"),
            new ObjectParameter("dbo.Run Report", 1, "@id", "int", null, false, "report.sql"),
        };

        var skeleton = SqlTestSkeletonBuilder.Build("dbo.Run Report", parameters);

        Assert.Equal(["@id", "@name"], skeleton.Parameters);
        Assert.Equal(
            "EXEC [dbo].[Run Report]\n    @id = NULL,\n    @name = NULL;",
            skeleton.Sql
        );
    }

    [Fact]
    public void Build_WithoutParametersProducesSingleExecStatement()
    {
        var skeleton = SqlTestSkeletonBuilder.Build("dbo.HealthCheck", []);

        Assert.Equal("EXEC [dbo].[HealthCheck];", skeleton.Sql);
    }

    [Fact]
    public void Build_RejectsOutputParameters()
    {
        var parameters = new[]
        {
            new ObjectParameter("dbo.Run", 1, "@result", "int", null, true, "run.sql"),
        };

        var exception = Assert.Throws<ArgumentException>(() => SqlTestSkeletonBuilder.Build("dbo.Run", parameters));

        Assert.Contains("OUTPUT parameters require caller variables", exception.Message);
    }
}
