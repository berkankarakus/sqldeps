using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Lsp;
using SqlDeps.Storage;
using SqlDeps.Tests;

namespace SqlDeps.Tests.Lsp;

public sealed class WorkspaceIndexLocatorTests : SqlFolderTestBase
{
    [Fact]
    public void SourceRoot_ReturnsNullForCorruptIndex()
    {
        var path = Path.Combine(_folder, "corrupt.sqlite");

        File.WriteAllText(path, "not a SQLite database");

        Assert.Null(WorkspaceIndexLocator.SourceRoot(path));
    }

    [Fact]
    public void SourceRoot_ReturnsStoredWorkspaceRootForValidIndex()
    {
        var path = Path.Combine(_folder, "valid.sqlite");
        SqliteStore.Write(
            path,
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
            settings: "",
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        Assert.Equal(Path.GetFullPath(_folder), WorkspaceIndexLocator.SourceRoot(path));
    }
}
