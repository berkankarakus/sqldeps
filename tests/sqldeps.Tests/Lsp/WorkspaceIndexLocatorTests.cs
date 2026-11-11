using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class WorkspaceIndexLocatorTests
{
    [Fact]
    public void SourceRoot_ReturnsNullForCorruptIndex()
    {
        var path = Path.Combine(Path.GetTempPath(), "sqldeps-corrupt-index-" + Guid.NewGuid() + ".sqlite");

        try
        {
            File.WriteAllText(path, "not a SQLite database");

            Assert.Null(WorkspaceIndexLocator.SourceRoot(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
