using SqlDeps;

namespace SqlDeps.Tests;

public abstract class SqlFolderTestBase : IDisposable
{
    protected readonly string _folder = Path.Combine(Path.GetTempPath(), "sqldeps-tests-" + Guid.NewGuid());

    protected SqlFolderTestBase()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }
}
