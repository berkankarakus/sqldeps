using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Storage;

using System.Diagnostics.CodeAnalysis;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void StartWorkspaceLoad()
    {
        var root = workspaceAnalysisRoot;
        if (root is null)
        {
            return;
        }

        var loadVersion = Interlocked.Increment(ref workspaceLoadVersion);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref workspaceLoadCancellation, cancellation);
        previous?.Cancel();
        var selectedIndex = selectedIndexPath;
        var allowAutomaticSelection = allowAutomaticIndexSelection;
        workspaceLoadTask = Task.Run(
            () => LoadWorkspaceAsync(root, loadVersion, cancellation.Token, selectedIndex, allowAutomaticSelection)
        );
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Workspace loading reports failures and keeps the LSP process available for live documents.")]
    private async Task LoadWorkspaceAsync(
        string root,
        int loadVersion,
        CancellationToken cancellationToken,
        string? selectedIndexPath,
        bool allowAutomaticSelection
    )
    {
        var entered = false;
        try
        {
            await workspaceLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
            {
                return;
            }

            var loaded = BuildWorkspaceAnalysis(
                root,
                cancellationToken,
                selectedIndexPath,
                allowAutomaticSelection
            );
            cancellationToken.ThrowIfCancellationRequested();

            if (loadVersion != Volatile.Read(ref workspaceLoadVersion))
            {
                return;
            }

            lock (gate)
            {
                if (loadVersion != workspaceLoadVersion)
                {
                    return;
                }

                workspace = loaded;
                InvalidateWorkspaceMetadataSnapshotLocked();
            }

            RefreshOpenDocuments();
            RequestDiagnosticRefresh();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(
                "sqldeps-lsp: workspace index failed: " + exception.Message
            ).ConfigureAwait(false);
        }
        finally
        {
            if (entered)
            {
                workspaceLoadGate.Release();
            }
        }
    }

    private static AnalysisResult BuildWorkspaceAnalysis(
        string root,
        CancellationToken cancellationToken,
        string? selectedIndexPath,
        bool allowAutomaticSelection
    )
    {
        var indexer = CreateWorkspaceIndexer(root, selectedIndexPath, allowAutomaticSelection);
        indexer.Index(root);
        cancellationToken.ThrowIfCancellationRequested();

        AnalysisResult loaded = new(root);
        AddIndexedWorkspaceData(loaded, root, indexer);

        return loaded;
    }

    private static SqlFolderIndexer CreateWorkspaceIndexer(
        string root,
        string? selectedIndexPath,
        bool allowAutomaticSelection
    )
    {
        var defaultSchema = ObjectNames.StandardDefaultSchema;
        var cache = new Dictionary<string, CachedFile>(StringComparer.Ordinal);
        Dictionary<string, List<CachedBatch>> batchCache = new(StringComparer.Ordinal);
        var databasePath = WorkspaceIndexLocator.Find(
            root,
            selectedIndexPath,
            allowAutomaticSelection
        );

        if (databasePath is not null)
        {
            var database = new IndexDatabase(databasePath);
            defaultSchema = database.DefaultSchema;
            (cache, batchCache) = IndexCache.LoadAll(databasePath, IndexCache.SettingsFor(defaultSchema, root));
        }

        return new SqlFolderIndexer(defaultSchema, cache, batchCache: batchCache);
    }

    private static void AddIndexedWorkspaceData(AnalysisResult loaded, string root, SqlFolderIndexer indexer)
    {
        loaded.Objects.AddRange(indexer.Objects.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.Dependencies.AddRange(indexer.Dependencies.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.ColumnUsage.AddRange(indexer.ColumnUsage.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.Parameters.AddRange(indexer.Parameters.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.ResultColumns.AddRange(indexer.ResultColumns.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.TableKeys.AddRange(indexer.TableKeys.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.IndexColumns.AddRange(indexer.IndexColumns.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.ForeignKeys.AddRange(indexer.ForeignKeys.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.Files.AddRange(indexer.Files.Select(item => item with { Path = AbsoluteSource(root, item.Path) }));
        loaded.TableColumns.AddRange(indexer.TableColumns.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.Problems.AddRange(indexer.Problems.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.LintFindings.AddRange(indexer.LintFindings.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
    }
}
