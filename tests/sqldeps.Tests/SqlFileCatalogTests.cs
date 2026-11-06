using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlFileCatalogTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "sqldeps-file-catalog-" + Guid.NewGuid().ToString("N")
    );

    public SqlFileCatalogTests()
    {
        Directory.CreateDirectory(folder);
    }

    [Fact]
    public void FindSqlFiles_ReturnsSqlFilesAndSkipsGeneratedFolders()
    {
        WriteSqlFile("procedure.sql");
        WriteSqlFile(Path.Combine("nested", "query.sql"));
        WriteSqlFile(Path.Combine(".git", "ignored.sql"));
        WriteSqlFile(Path.Combine("bin", "ignored.sql"));
        WriteSqlFile(Path.Combine("node_modules", "ignored.sql"));

        var files = SqlFileCatalog.FindSqlFiles(folder)
            .Select(file => Path.GetRelativePath(folder, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "nested" + Path.DirectorySeparatorChar + "query.sql", "procedure.sql" },
            files
        );
    }

    [Fact]
    public void FindSqlFiles_DoesNotSkipSqlFilesInOrdinaryFolders()
    {
        WriteSqlFile(Path.Combine("scripts", "query.sql"));
        WriteSqlFile(Path.Combine("object", "view.sql"));

        var files = SqlFileCatalog.FindSqlFiles(folder).ToArray();

        Assert.Equal(2, files.Length);
        Assert.All(files, file => Assert.EndsWith(".sql", file, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private void WriteSqlFile(string relativePath)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "SELECT 1;");
    }
}
