using Microsoft.Data.Sqlite;
using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Storage;

namespace SqlDeps.Tests;

public sealed class SqlIndexerMetadataTests : SqlFolderTestBase
{
    [Fact]
    public void Index_ComputedTableColumn_IsRecordedWithoutStoppingTheFile()
    {
        File.WriteAllText(Path.Combine(_folder, "computed.sql"), """
            CREATE TABLE dbo.OrderLine
            (
                Quantity int NOT NULL,
                Total AS Quantity * 2
            );
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Collection(
            indexer.TableColumns.OrderBy(column => column.ColumnName),
            quantity =>
            {
                Assert.Equal("Quantity", quantity.ColumnName);
                Assert.Equal("int", quantity.DataType, ignoreCase: true);
            },
            total =>
            {
                Assert.Equal("Total", total.ColumnName);
                Assert.Equal("computed", total.DataType);
            }
        );
    }

    [Fact]
    public void Index_AlterTableAdd_RecordsTheAddedColumn()
    {
        File.WriteAllText(Path.Combine(_folder, "alter.sql"), "ALTER TABLE dbo.OrderLine ADD Discount decimal(10, 2) NULL;");
        File.WriteAllText(Path.Combine(_folder, "repeat-alter.sql"), "ALTER TABLE dbo.OrderLine ADD Discount decimal(10, 2) NULL;");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.Equal("dbo.OrderLine", column.TableName);
        Assert.Equal("Discount", column.ColumnName);
        Assert.Equal("decimal (10, 2)", column.DataType, ignoreCase: true);
    }

    [Fact]
    public void Index_AlterTableAlterColumn_RecordsTheColumnType()
    {
        File.WriteAllText(Path.Combine(_folder, "alter-column.sql"), "ALTER TABLE dbo.OrderLine ALTER COLUMN Discount decimal(12, 4) NOT NULL;");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.Equal("dbo.OrderLine", column.TableName);
        Assert.Equal("Discount", column.ColumnName);
        Assert.Equal("decimal (12, 4)", column.DataType, ignoreCase: true);
    }

    [Fact]
    public void Index_TableColumnMetadata_PreservesNullabilityIdentityDefaultsComputedPrecisionScaleAndCollation()
    {
        File.WriteAllText(Path.Combine(_folder, "metadata.sql"), """
            CREATE TABLE dbo.MetadataProbe
            (
                id INT IDENTITY(1, 1) NOT NULL,
                amount DECIMAL(10, 2) NULL,
                label VARCHAR(20) COLLATE Latin1_General_100_CI_AS NOT NULL DEFAULT 'new',
                computed_value AS id + 1
            );
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var id = Assert.Single(indexer.TableColumns, column => column.ColumnName == "id");
        Assert.False(id.IsNullable);
        Assert.True(id.IsIdentity);
        Assert.Equal(10, indexer.TableColumns.Single(column => column.ColumnName == "amount").Precision);
        Assert.Equal(2, indexer.TableColumns.Single(column => column.ColumnName == "amount").Scale);

        var label = indexer.TableColumns.Single(column => column.ColumnName == "label");
        Assert.False(label.IsNullable);
        Assert.Equal("'new'", label.DefaultValue);
        Assert.Equal("Latin1_General_100_CI_AS", label.Collation);

        var computed = indexer.TableColumns.Single(column => column.ColumnName == "computed_value");
        Assert.True(computed.IsComputed);
        Assert.Equal("computed", computed.DataType);
    }

    [Fact]
    public void Index_AlterColumn_NotNullOverrideIsRetainedInMetadata()
    {
        File.WriteAllText(Path.Combine(_folder, "alter-nullability.sql"), "ALTER TABLE dbo.MetadataProbe ALTER COLUMN amount DECIMAL(12, 4) NOT NULL;");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.False(column.IsNullable);
        Assert.Equal("decimal (12, 4)", column.DataType, ignoreCase: true);
        Assert.Equal(12, column.Precision);
        Assert.Equal(4, column.Scale);
    }

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

    [Fact]
    public void Index_RecordsTsqlLintFindings()
    {
        File.WriteAllText(Path.Combine(_folder, "lint.sql"), """
            SELECT * FROM dbo.Orders WITH (NOLOCK);
            SELECT Id FROM dbo.Orders WITH (READUNCOMMITTED);
            UPDATE dbo.Orders SET Status = 'Archived';
            DELETE FROM dbo.Orders;
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET Status = 'Archived';
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "select-star" && finding.Severity == "info");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "unfiltered-select" && finding.Severity == "info");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "nolock" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "read-uncommitted" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "update-without-where" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "delete-without-where" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "merge" && finding.Severity == "warning");
        Assert.All(indexer.LintFindings, finding =>
        {
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }
}
