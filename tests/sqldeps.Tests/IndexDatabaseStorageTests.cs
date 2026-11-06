using Microsoft.Data.Sqlite;
using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Tests;

public sealed class IndexDatabaseStorageTests : SqlFolderTestBase
{
    [Fact]
    public void GetStatus_ChangedFile_ReportsExactlyOneChangedFile()
    {
        const string relativePath = "procedure.sql";
        var sourcePath = Path.Combine(_folder, relativePath);
        File.WriteAllText(sourcePath, "SELECT 1;");
        var originalHash = SqlFileReader.HashOf(File.ReadAllBytes(sourcePath));
        var databasePath = Path.Combine(_folder, "index.sqlite");
        var settings = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile(relativePath, originalHash)],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: settings,
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

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
    public void SqliteStore_WritesSchemaVersionMetadata()
    {
        var databasePath = Path.Combine(_folder, "schema-version.sqlite");
        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var value = new IndexDatabase(databasePath).Query("SELECT value FROM meta WHERE key = 'schema_version'");

        Assert.Equal(SqliteStore.SchemaVersion.ToString(), value.Single()["value"]?.ToString());
    }

    [Fact]
    public void SqliteStore_OpensDatabasePathContainingConnectionStringCharacters()
    {
        var databasePath = Path.Combine(_folder, "index;mode=memory.sqlite");

        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var value = new IndexDatabase(databasePath).Query("SELECT value FROM meta WHERE key = 'schema_version'");

        Assert.Equal(SqliteStore.SchemaVersion.ToString(), value.Single()["value"]?.ToString());
    }

    [Fact]
    public void IndexCache_RejectsAnIncompatibleSchemaVersion()
    {
        var databasePath = Path.Combine(_folder, "old-schema.sqlite");
        var settings = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Cached", "table", "cached.sql", 1)],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile("cached.sql", "hash")],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: settings,
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        using (var connection = new SqliteConnection("Data Source=" + databasePath))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE meta SET value = '0' WHERE key = 'schema_version'";
            command.ExecuteNonQuery();
        }

        Assert.Empty(IndexCache.Load(databasePath, settings));
    }

    [Fact]
    public void IndexCache_RoundTripsNamedMetadataRows()
    {
        var databasePath = Path.Combine(_folder, "metadata-cache.sqlite");
        const string relativePath = "metadata.sql";
        const string settings = "rules=19;default_schema=dbo";
        var resultColumn = new ResultColumn("dbo.Report", 2, "total", relativePath, 7, false, "decimal (10, 2)");
        var indexColumn = new TableIndexColumn("dbo.Orders", "IX_Orders_Status", "status", 1, false, true, true, false, true, "status = 'active'", relativePath, 11);
        var tableColumn = new TableColumn("dbo.Orders", "status", "varchar (20)", relativePath, 4, false, true, "'new'", false, 20, null, "Latin1_General_100_CI_AS");

        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [resultColumn],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [indexColumn],
            columnUsage: [],
            tableColumns: [tableColumn],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile(relativePath, "hash")],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: settings,
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var cached = Assert.Single(IndexCache.Load(databasePath, settings).Values);

        Assert.Equal(resultColumn, Assert.Single(cached.ResultColumns));
        Assert.Equal(indexColumn, Assert.Single(cached.IndexColumns));
        Assert.Equal(tableColumn, Assert.Single(cached.TableColumns));
    }

    [Fact]
    public void IndexCache_RoundTripsBatchContributions()
    {
        var databasePath = Path.Combine(_folder, "batch-cache.sqlite");
        const string relativePath = "batch.sql";
        CachedBatch batch = new(relativePath, 1, 4, "batch-hash", "batch-payload");

        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder,
            batches: [batch]);

        KeyValuePair<string, List<CachedBatch>> cached = Assert.Single(
            IndexCache.LoadBatches(databasePath, IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema)));

        Assert.Equal(batch, Assert.Single(cached.Value));
    }

    [Fact]
    public void IndexQueryService_ExposesWhitelistedNamedOperations()
    {
        var databasePath = Path.Combine(_folder, "query-service.sqlite");
        SqliteStore.Write(
            databasePath,
            objects: [new DatabaseObject("dbo.Source", "procedure", "source.sql", 1)],
            dependencies: [new Dependency("dbo.Source", "dbo.Target", "SELECT", "source.sql", 2)],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var service = new IndexQueryService(new IndexDatabase(databasePath));
        var rows = service.ObjectQuery("deps", "dbo.Source");

        Assert.Single(rows);
        Assert.Equal("dbo.Target", rows[0]["target_object"]?.ToString());
        Assert.Throws<ArgumentException>(() => service.ObjectQuery("arbitrary-sql", "dbo.Source"));
    }

    [Fact]
    public void QualifyObjectName_OnePartName_UsesIndexedDefaultSchema()
    {
        var databasePath = Path.Combine(_folder, "index.sqlite");
        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: "rules=5;default_schema=app",
            defaultSchema: "app",
            sourceRoot: _folder);

        var database = new IndexDatabase(databasePath);

        Assert.Equal("app.Orders", database.QualifyObjectName("Orders"));
        Assert.Equal("audit.Orders", database.QualifyObjectName("audit.Orders"));
    }
}
