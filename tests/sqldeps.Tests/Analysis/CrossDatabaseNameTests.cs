using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Tests.Analysis;

public sealed class CrossDatabaseNameTests
{
    [Fact]
    public void Collect_UsesDefaultDatabaseForUnqualifiedObjectNames()
    {
        const string sql = "CREATE PROCEDURE dbo.ReadOrders AS SELECT * FROM dbo.Orders;";
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        TSqlScript script = (TSqlScript)parser.Parse(new StringReader(sql), out IList<ParseError> errors);

        Assert.Empty(errors);

        var result = DependencyCollectionAnalyzer.Collect(
            script,
            "orders.sql",
            1,
            "dbo",
            sql,
            "catalogDb");

        var procedure = Assert.Single(result.Contribution.Objects);
        var dependency = Assert.Single(result.Contribution.Dependencies);

        Assert.Equal("catalogDb.dbo.ReadOrders", procedure.Name);
        Assert.Equal("catalogDb.dbo.ReadOrders", dependency.SourceObject);
        Assert.Equal("catalogDb.dbo.Orders", dependency.TargetObject);
    }

    [Fact]
    public void DocumentAnalysis_UsesDefaultDatabaseForLiveDocumentDependencies()
    {
        var result = DocumentAnalysis.Analyze(
            "orders.sql",
            "SELECT * FROM dbo.Orders;",
            "dbo",
            "catalogDb");

        var dependency = Assert.Single(result.Dependencies);

        Assert.Equal("catalogDb.dbo.Orders", dependency.TargetObject);
    }

    [Fact]
    public void DocumentDiagnostics_UsesDefaultDatabaseForIndexedColumns()
    {
        var metadata = new MetadataCatalog(
            [],
            [new TableColumn("catalogDb.dbo.Orders", "OrderId", "int", "schema.sql", 1)],
            [],
            [],
            []);

        var result = DocumentDiagnosticAnalyzer.Analyze(
            "orders.sql",
            "SELECT OrderId FROM dbo.Orders;",
            metadata,
            [],
            [],
            "dbo",
            defaultDatabase: "catalogDb");

        Assert.DoesNotContain(result.Diagnostics, finding => finding.Rule is "unknown-table" or "unknown-column");
    }

    [Theory]
    [InlineData("[catalogDb].[dbo].[Orders]", "catalogDb.dbo.Orders")]
    [InlineData("[192.168.5.2].[catalogDb].[dbo].[Orders]", "192.168.5.2.catalogDb.dbo.Orders")]
    public void Qualify_NormalizesQuotedMultipartNamesWithoutApplyingDefaults(
        string name,
        string expected)
    {
        Assert.Equal(expected, ObjectNames.Qualify(name, "custom", "fallbackDb"));
    }

    [Fact]
    public void Qualify_LeavesMalformedQuotedNamesUnresolved()
    {
        Assert.Equal("[catalogDb].[dbo.Orders", ObjectNames.Qualify(
            "[catalogDb].[dbo.Orders",
            "custom",
            "fallbackDb"));
    }
}
