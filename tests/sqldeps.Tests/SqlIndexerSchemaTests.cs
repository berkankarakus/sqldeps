using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlIndexerSchemaTests : SqlFolderTestBase
{
    [Fact]
    public void Index_TableConstraints_RecordsPrimaryAndUniqueColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "keys.sql"), "CREATE TABLE dbo.Orders (id INT NOT NULL, code INT NOT NULL, CONSTRAINT PK_Orders PRIMARY KEY (id), CONSTRAINT UQ_Orders_Code UNIQUE (code));");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.TableKeys, key => key.Kind == "primary-key" && key.ColumnName == "id");
        Assert.Contains(indexer.TableKeys, key => key.Kind == "unique" && key.ColumnName == "code");
    }

    [Fact]
    public void Index_TableConstraints_RecordsForeignKeyTargets()
    {
        File.WriteAllText(Path.Combine(_folder, "foreign-key.sql"), "CREATE TABLE dbo.OrderLine (order_id INT, CONSTRAINT FK_OrderLine_Order FOREIGN KEY (order_id) REFERENCES dbo.Orders (id));");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var foreignKey = Assert.Single(indexer.ForeignKeys);
        Assert.Equal("dbo.OrderLine", foreignKey.TableName);
        Assert.Equal("order_id", foreignKey.ColumnName);
        Assert.Equal("dbo.Orders", foreignKey.ReferencedTableName);
        Assert.Equal("id", foreignKey.ReferencedColumnName);
    }

    [Fact]
    public void Index_CreateIndex_RecordsKeyAndIncludedColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "index.sql"), "CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (code DESC) INCLUDE (id);");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.IndexColumns, column => column.IndexName == "IX_Orders_Code" && column.ColumnName == "code" && column.IsDescending);
        Assert.Contains(indexer.IndexColumns, column => column.IndexName == "IX_Orders_Code" && column.ColumnName == "id" && column.IsIncluded);
    }

    [Fact]
    public void Index_AlterTableConstraint_RecordsForeignKey()
    {
        File.WriteAllText(Path.Combine(_folder, "alter-constraint.sql"), "ALTER TABLE dbo.OrderLine ADD CONSTRAINT FK_OrderLine_Order FOREIGN KEY (order_id) REFERENCES dbo.Orders (id);");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.ForeignKeys, key => key.TableName == "dbo.OrderLine" && key.ReferencedTableName == "dbo.Orders");
    }
}
