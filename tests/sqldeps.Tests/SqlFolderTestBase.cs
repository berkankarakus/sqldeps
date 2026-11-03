using SqlDeps;
using SqlDeps.Domain;

namespace SqlDeps.Tests;

public abstract class SqlFolderTestBase : IDisposable
{
    protected readonly string _folder = Path.Combine(Path.GetTempPath(), "sqldeps-tests-" + Guid.NewGuid());

    protected SqlFolderTestBase()
    {
        Directory.CreateDirectory(_folder);
    }

    protected SqlFolderIndexer CreateIndexer()
    {
        return new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }
}
