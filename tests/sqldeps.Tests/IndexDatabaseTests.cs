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
        SqliteStore.Write(databasePath, [], [], [], [], [], [new IndexedFile(relativePath, originalHash)], ReachabilityBuilder.Build([], 12), settings);

        File.WriteAllText(sourcePath, "SELECT 2;");

        var status = new IndexDatabase(databasePath).GetStatus(_folder);

        Assert.Equal(1, status.SqlFiles);
        Assert.Equal(1, status.IndexedFiles);
        Assert.Equal(0, status.Added);
        Assert.Equal(1, status.Changed);
        Assert.Equal(0, status.Removed);
        Assert.False(status.IsCurrent);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }
}
