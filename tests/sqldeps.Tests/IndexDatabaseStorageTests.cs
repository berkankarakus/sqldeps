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
    public void IndexCache_SettingsChangeWhenRepositorySuppressionChanges()
    {
        string withoutSuppression = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, _folder);

        File.WriteAllText(Path.Combine(_folder, ".sqldeps"), "sqldeps: disable-repo=nolock\n");

        string withSuppression = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, _folder);

        Assert.NotEqual(withoutSuppression, withSuppression);
    }

    [Fact]
    public void IndexCache_SettingsChangeWhenDefaultDatabaseChanges()
    {
        string withoutDatabase = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        string withDatabase = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema, defaultDatabase: "catalogDb");

        Assert.NotEqual(withoutDatabase, withDatabase);
    }

    [Fact]
    public void IndexCache_SettingsChangeWhenDiscoveryScopeChanges()
    {
        string withoutScope = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        string withScope = IndexCache.SettingsFor(
            ObjectNames.StandardDefaultSchema,
            includePatterns: ["**/Production/**"],
            excludePatterns: ["**/Archive/**"]);

        Assert.NotEqual(withoutScope, withScope);
    }

    [Fact]
    public void SqliteStore_RoundTripsDiscoveryPatterns()
    {
        var databasePath = Path.Combine(_folder, "discovery-policy.sqlite");

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
            settings: IndexCache.SettingsFor(
                ObjectNames.StandardDefaultSchema,
                defaultDatabase: "catalogDb",
                includePatterns: ["**/Production/**"],
                excludePatterns: ["**/Archive/**"]),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder,
            includePatterns: ["**/Production/**"],
            excludePatterns: ["**/Archive/**"]);

        var database = new IndexDatabase(databasePath);

        Assert.Equal(["**/Production/**"], database.IncludePatterns);
        Assert.Equal(["**/Archive/**"], database.ExcludePatterns);
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
    public void GetStatus_ReportsTheDiscoveryScopeUsedByTheIndex()
    {
        File.WriteAllText(Path.Combine(_folder, "procedure.sql"), "SELECT 1;");
        var databasePath = Path.Combine(_folder, "scoped-status.sqlite");
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
            includePatterns: ["**/Production/**"],
            excludePatterns: ["**/Archive/**"]);

        var status = new IndexDatabase(databasePath).GetStatus(_folder);

        Assert.Equal(["**/Production/**"], status.IncludePatterns);
        Assert.Equal(["**/Archive/**"], status.ExcludePatterns);
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
    public void IndexQueryService_ReturnsDirectAndRecursiveCallerEdges()
    {
        var databasePath = Path.Combine(_folder, "callers.sqlite");
        var dependencies = new[]
        {
            new Dependency("dbo.Admin", "dbo.Process", "EXEC", "admin.sql", 4),
            new Dependency("dbo.Process", "dbo.Orders", "SELECT", "process.sql", 8),
        };

        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: dependencies,
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
            reachability: ReachabilityBuilder.Build(dependencies, 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var service = new IndexQueryService(new IndexDatabase(databasePath));
        var direct = service.Callers("dbo.Orders", recursive: false, maxDepth: 1);
        var recursive = service.Callers("dbo.Orders", recursive: true, maxDepth: 10);

        Assert.Single(direct);
        Assert.Equal("dbo.Process", direct[0]["source"]?.ToString());
        Assert.Equal(3, recursive.Count);
        Assert.Contains(recursive, row => row["source"]?.ToString() == "dbo.Admin" && Convert.ToInt32(row["depth"]) == 1);
        Assert.Contains(recursive, row => row["target"]?.ToString() == "dbo.Orders" && Convert.ToInt32(row["depth"]) == 2);
        Assert.All(recursive, row => Assert.Contains("->", row["path"]?.ToString()));
        Assert.All(recursive, row => Assert.Equal("edge", row["kind"]?.ToString()));
        Assert.DoesNotContain(recursive, row => Convert.ToBoolean(row["cycle"]));
        Assert.DoesNotContain(recursive, row => Convert.ToBoolean(row["truncated"]));
    }

    [Fact]
    public void IndexQueryService_ReportsCallerCyclesAndDepthTruncation()
    {
        var databasePath = Path.Combine(_folder, "caller-cycles.sqlite");
        var dependencies = new[]
        {
            new Dependency("dbo.Admin", "dbo.Process", "EXEC", "admin.sql", 4),
            new Dependency("dbo.Process", "dbo.Admin", "EXEC", "process.sql", 8),
            new Dependency("dbo.Process", "dbo.Orders", "SELECT", "process.sql", 9),
            new Dependency("dbo.External", "dbo.Admin", "EXEC", "external.sql", 12),
        };

        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: dependencies,
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
            reachability: ReachabilityBuilder.Build(dependencies, 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var service = new IndexQueryService(new IndexDatabase(databasePath));
        var cycleRows = service.Callers("dbo.Orders", recursive: true, maxDepth: 2);
        var truncatedRows = service.Callers("dbo.Orders", recursive: true, maxDepth: 1);

        Assert.Contains(cycleRows, row =>
            row["kind"]?.ToString() == "cycle"
            && Convert.ToBoolean(row["cycle"])
            && row["root"]?.ToString() == "dbo.Process");
        Assert.Contains(truncatedRows, row =>
            row["kind"]?.ToString() == "truncation"
            && Convert.ToBoolean(row["truncated"])
            && row["root"]?.ToString() == "dbo.Process");
    }

    [Fact]
    public void IndexQueryService_ReturnsDynamicSqlCandidatesWithoutGraphEdges()
    {
        var databasePath = Path.Combine(_folder, "dynamic-candidates.sqlite");
        var candidates = new[]
        {
            new DynamicSqlCandidate(
                "dbo.Source",
                "dbo.Target",
                "SELECT",
                "literal",
                "Explicit object name found in a literal dynamic SQL fragment.",
                "source.sql",
                12),
        };

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
            dynamicSqlCandidates: candidates);

        var service = new IndexQueryService(new IndexDatabase(databasePath));
        var rows = service.DynamicSqlCandidates("dbo.Source");

        var row = Assert.Single(rows);
        Assert.Equal("dbo.Target", row["candidate_object"]?.ToString());
        Assert.Equal("literal", row["confidence"]?.ToString());
        Assert.Equal(12, Convert.ToInt32(row["line"]));
    }

    [Fact]
    public void IndexQueryService_IsIndexCurrentTracksSourceFileChanges()
    {
        const string relativePath = "current.sql";
        var sourcePath = Path.Combine(_folder, relativePath);
        File.WriteAllText(sourcePath, "SELECT 1;");
        var databasePath = Path.Combine(_folder, "current.sqlite");
        var hash = SqlFileReader.HashOf(File.ReadAllBytes(sourcePath));

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
            files: [new IndexedFile(relativePath, hash)],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema),
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        var service = new IndexQueryService(new IndexDatabase(databasePath));

        Assert.True(service.IsIndexCurrent());

        File.WriteAllText(sourcePath, "SELECT 2;");

        Assert.False(service.IsIndexCurrent());
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

    [Fact]
    public void QualifyObjectName_UsesIndexedDefaultDatabaseForOneAndTwoPartNames()
    {
        var databasePath = Path.Combine(_folder, "cross-database.sqlite");
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
            settings: IndexCache.SettingsFor("custom", defaultDatabase: "catalogDb"),
            defaultSchema: "custom",
            sourceRoot: _folder,
            defaultDatabase: "catalogDb");

        var database = new IndexDatabase(databasePath);

        Assert.Equal("catalogDb", database.DefaultDatabase);
        Assert.Equal("catalogDb.custom.Products", database.QualifyObjectName("Products"));
        Assert.Equal("catalogDb.sales.Orders", database.QualifyObjectName("sales.Orders"));
        Assert.Equal("otherDb.sales.Orders", database.QualifyObjectName("otherDb.sales.Orders"));
    }
}
