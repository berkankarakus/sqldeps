using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Tests;

public sealed class IndexDatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sqldeps-tests-" + Guid.NewGuid());

    public IndexDatabaseTests()
    {
        Directory.CreateDirectory(_folder);
    }

    [Fact]
    public void GetStatus_ChangedFile_ReportsExactlyOneChangedFile()
    {
        const string relativePath = "procedure.sql";
        var sourcePath = Path.Combine(_folder, relativePath);
        File.WriteAllText(sourcePath, "SELECT 1;");
        var originalHash = SqlFileReader.HashOf(File.ReadAllBytes(sourcePath));
        var databasePath = Path.Combine(_folder, "index.sqlite");
        var settings = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        SqliteStore.Write(databasePath, [], [], [], [], [], [], [], [], [], [new IndexedFile(relativePath, originalHash)], ReachabilityBuilder.Build([], 12), settings, ObjectNames.StandardDefaultSchema, _folder);

        File.WriteAllText(sourcePath, "SELECT 2;");

        var status = new IndexDatabase(databasePath).GetStatus(_folder);

        Assert.Equal(1, status.SqlFiles);
        Assert.Equal(1, status.IndexedFiles);
        Assert.Equal(0, status.Added);
        Assert.Equal(1, status.Changed);
        Assert.Equal(0, status.Removed);
        Assert.False(status.IsCurrent);
    }

    [Fact]
    public void QualifyObjectName_OnePartName_UsesIndexedDefaultSchema()
    {
        var databasePath = Path.Combine(_folder, "index.sqlite");
        SqliteStore.Write(databasePath, [], [], [], [], [], [], [], [], [], [], ReachabilityBuilder.Build([], 12), "rules=5;default_schema=app", "app", _folder);

        var database = new IndexDatabase(databasePath);

        Assert.Equal("app.Orders", database.QualifyObjectName("Orders"));
        Assert.Equal("audit.Orders", database.QualifyObjectName("audit.Orders"));
    }

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
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

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
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

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
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.Equal("dbo.OrderLine", column.TableName);
        Assert.Equal("Discount", column.ColumnName);
        Assert.Equal("decimal (12, 4)", column.DataType, ignoreCase: true);
    }

    [Fact]
    public void Index_ProcedureTopLevelSelect_RecordsResultColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "results.sql"), "CREATE PROCEDURE dbo.GetOrders AS SELECT id, name AS display_name FROM dbo.Orders;");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

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
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

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

    [Fact]
    public void Index_SuppressesOnlyTheRuleOnTheNextLine()
    {
        File.WriteAllText(Path.Combine(_folder, "suppressed.sql"), """
            -- sqldeps: disable-next-line=merge
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET Status = 'Archived';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge");
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }
}
