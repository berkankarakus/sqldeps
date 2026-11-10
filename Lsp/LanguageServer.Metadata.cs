using System.Diagnostics.CodeAnalysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private IEnumerable<(DatabaseObject Object, string Uri)> AllObjects()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Objects;
        }
    }

    private static bool IsTableOrView([NotNullWhen(true)] DatabaseObject? databaseObject)
    {
        if (databaseObject is null)
        {
            return false;
        }

        return databaseObject.Kind is "table" or "view";
    }

    private WorkspaceMetadataSnapshot MetadataSnapshotLocked()
    {
        if (workspaceMetadataSnapshot is not null
            && workspaceMetadataSnapshot.Revision == workspaceMetadataRevision)
        {
            return workspaceMetadataSnapshot;
        }

        var openPaths = openDocuments.Values
            .Select(document => document.SourceFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var savedObjects = workspace?.Objects.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedDependencies = workspace?.Dependencies.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedColumnUsage = workspace?.ColumnUsage.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedParameters = workspace?.Parameters.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedTableColumns = workspace?.TableColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedResultColumns = workspace?.ResultColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedTableKeys = workspace?.TableKeys.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedIndexColumns = workspace?.IndexColumns.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];
        var savedForeignKeys = workspace?.ForeignKeys.Where(item => !openPaths.Contains(item.SourceFile)) ?? [];

        workspaceMetadataSnapshot = new WorkspaceMetadataSnapshot(
            workspaceMetadataRevision,
            savedObjects.Concat(openDocuments.Values.SelectMany(document => document.Objects))
                .Select(item => (item, LspUri.FromPath(item.SourceFile)))
                .ToArray(),
            savedDependencies.Concat(openDocuments.Values.SelectMany(document => document.Dependencies)).ToArray(),
            savedColumnUsage.Concat(openDocuments.Values.SelectMany(document => document.ColumnUsage)).ToArray(),
            savedParameters.Concat(openDocuments.Values.SelectMany(document => document.Parameters)).ToArray(),
            savedTableColumns.Concat(openDocuments.Values.SelectMany(document => document.TableColumns)).ToArray(),
            savedResultColumns.Concat(openDocuments.Values.SelectMany(document => document.ResultColumns)).ToArray(),
            savedTableKeys.Concat(openDocuments.Values.SelectMany(document => document.TableKeys)).ToArray(),
            savedIndexColumns.Concat(openDocuments.Values.SelectMany(document => document.IndexColumns)).ToArray(),
            savedForeignKeys.Concat(openDocuments.Values.SelectMany(document => document.ForeignKeys)).ToArray());

        return workspaceMetadataSnapshot;
    }

    private void InvalidateWorkspaceMetadataSnapshotLocked()
    {
        workspaceMetadataRevision++;
        workspaceMetadataSnapshot = null;
        diagnosticCache.Clear();
    }

    private IEnumerable<Dependency> AllDependencies()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Dependencies;
        }
    }

    private IEnumerable<ColumnUsage> AllColumnUsage()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().ColumnUsage;
        }
    }

    private IEnumerable<ObjectParameter> AllParameters()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().Parameters;
        }
    }

    private IEnumerable<TableColumn> AllTableColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().TableColumns;
        }
    }

    private IEnumerable<ResultColumn> AllResultColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().ResultColumns;
        }
    }

    private IEnumerable<TableKey> AllTableKeys()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().TableKeys;
        }
    }

    private IEnumerable<TableIndexColumn> AllIndexColumns()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().IndexColumns;
        }
    }

    private IEnumerable<TableForeignKey> AllForeignKeys()
    {
        lock (gate)
        {
            return MetadataSnapshotLocked().ForeignKeys;
        }
    }
}
