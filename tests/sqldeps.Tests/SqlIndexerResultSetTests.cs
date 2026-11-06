using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlIndexerResultSetTests : SqlFolderTestBase
{
    [Fact]
    public void Index_ProcedureTopLevelSelect_RecordsResultColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "results.sql"), "CREATE PROCEDURE dbo.GetOrders AS SELECT id, name AS display_name FROM dbo.Orders;");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Collection(
            indexer.ResultColumns.OrderBy(column => column.Ordinal),
            id =>
            {
                Assert.Equal("dbo.GetOrders", id.ObjectName);
                Assert.Equal("id", id.ColumnName);
                Assert.Equal(0, id.Ordinal);
            },
            displayName => Assert.Equal("display_name", displayName.ColumnName));
    }

    [Fact]
    public void Index_ClrProcedureWithoutStatementList_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_folder, "clr-procedure.sql"), "CREATE PROCEDURE dbo.ManagedProc AS EXTERNAL NAME [MyAssembly].[MyType].[Run];");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.Objects, item => item.Name == "dbo.ManagedProc" && item.Kind == "procedure");
        Assert.Empty(indexer.ResultColumns);
    }

    [Fact]
    public void Index_ProcedureUnion_UsesFirstBranchResultNames()
    {
        File.WriteAllText(Path.Combine(_folder, "union-results.sql"), "CREATE PROCEDURE dbo.GetIds AS SELECT id AS item_id FROM dbo.Orders UNION SELECT id FROM dbo.ArchivedOrders;");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var column = Assert.Single(indexer.ResultColumns);
        Assert.Equal("item_id", column.ColumnName);
        Assert.Equal(0, column.Ordinal);
    }

    [Fact]
    public void Index_ProcedureWithoutSelectStatementDoesNotCreateResultColumns()
    {
        File.WriteAllText(
            Path.Combine(_folder, "no-result.sql"),
            "CREATE PROCEDURE dbo.NoResult AS BEGIN SET NOCOUNT ON; RETURN; END;"
        );
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.Objects, item => item.Name == "dbo.NoResult" && item.Kind == "procedure");
        Assert.Empty(indexer.ResultColumns);
    }

    [Fact]
    public void Index_ScalarFunctionDoesNotCreateResultColumns()
    {
        File.WriteAllText(
            Path.Combine(_folder, "scalar-function.sql"),
            "CREATE FUNCTION dbo.GetValue() RETURNS INT AS BEGIN RETURN 1; END;"
        );
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.Objects, item => item.Name == "dbo.GetValue" && item.Kind == "function");
        Assert.Empty(indexer.ResultColumns);
    }
}
