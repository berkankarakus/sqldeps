using SqlDeps;
using SqlDeps.Domain;

namespace SqlDeps.Tests;

public sealed class MetadataCatalogTests
{
    [Fact]
    public void Constructor_SnapshotsEachMetadataSequence()
    {
        var columns = new List<TableColumn>
        {
            new("dbo.Orders", "id", "int", "orders.sql", 1)
        };

        var catalog = new MetadataCatalog(
            resultColumns: [],
            tableColumns: columns,
            tableKeys: [],
            foreignKeys: [],
            indexColumns: []);

        columns.Add(new TableColumn("dbo.Orders", "status", "varchar(20)", "orders.sql", 2));

        Assert.Single(catalog.TableColumns);
        Assert.Equal("id", catalog.TableColumns[0].ColumnName);
    }

    [Fact]
    public void Merge_PreservesAllMetadataFamilies()
    {
        var left = new MetadataCatalog(
            resultColumns: [new ResultColumn("dbo.GetOrders", 0, "id", "orders.sql", 1)],
            tableColumns: [new TableColumn("dbo.Orders", "id", "int", "orders.sql", 1)],
            tableKeys: [new TableKey("dbo.Orders", "primary-key", "PK_Orders", "id", 0, "orders.sql", 1)],
            foreignKeys: [],
            indexColumns: []);
        var right = new MetadataCatalog(
            resultColumns: [],
            tableColumns: [],
            tableKeys: [],
            foreignKeys: [new TableForeignKey("dbo.OrderLines", "FK_OrderLines_Orders", "order_id", "dbo.Orders", "id", 0, "lines.sql", 1)],
            indexColumns: [new TableIndexColumn("dbo.Orders", "IX_Orders_Status", "status", 0, false, false, false, false, false, null, "orders.sql", 3)]);

        var merged = left.Merge(right);

        Assert.Single(merged.ResultColumns);
        Assert.Single(merged.TableColumns);
        Assert.Single(merged.TableKeys);
        Assert.Single(merged.ForeignKeys);
        Assert.Single(merged.IndexColumns);
    }
}
