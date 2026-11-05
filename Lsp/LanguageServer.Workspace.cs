using SqlDeps.Domain;
using SqlDeps.Indexing;
using SqlDeps.Storage;

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
            () => LoadWorkspace(root, loadVersion, cancellation.Token, selectedIndex, allowAutomaticSelection)
        );
    }

    private void LoadWorkspace(
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
            workspaceLoadGate.Wait(cancellationToken);
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: workspace index failed: " + exception.Message);
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
        var databasePath = WorkspaceIndexLocator.Find(
            root,
            selectedIndexPath,
            allowAutomaticSelection
        );

        if (databasePath is not null)
        {
            var database = new IndexDatabase(databasePath);
            defaultSchema = database.DefaultSchema;
            cache = IndexCache.Load(databasePath, IndexCache.SettingsFor(defaultSchema));
        }

        return new SqlFolderIndexer(defaultSchema, cache);
    }

    private static void AddIndexedWorkspaceData(AnalysisResult loaded, string root, SqlFolderIndexer indexer)
    {
        loaded.Objects.AddRange(indexer.Objects.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
        loaded.Dependencies.AddRange(indexer.Dependencies.Select(item => item with { SourceFile = AbsoluteSource(root, item.SourceFile) }));
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

    private static string AbsoluteSource(string root, string relativePath) => Path.GetFullPath(Path.Combine(root, relativePath));

    private object WorkspaceDiagnostics()
    {
        workspaceLoadTask?.GetAwaiter().GetResult();

        AnalysisResult? loaded;
        HashSet<string> openFiles;
        lock (gate)
        {
            loaded = workspace is null ? null : Snapshot(workspace);
            openFiles = openDocuments.Values
                .Select(document => document.SourceFile)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (loaded is null)
        {
            return new { items = Array.Empty<object>() };
        }

        var staleFiles = StaleWorkspaceFiles(loaded);
        var groups = WorkspaceProblemGroups(loaded, staleFiles, openFiles)
            .Concat(WorkspaceLintGroups(loaded, staleFiles, openFiles))
            .GroupBy(group => group.Uri, StringComparer.Ordinal)
            .Select(group => new
            {
                uri = group.Key,
                version = (int?)null,
                kind = "full",
                items = group.SelectMany(item => item.Diagnostics).ToArray(),
            });

        return new { items = groups.ToArray() };
    }

    private static AnalysisResult Snapshot(AnalysisResult source)
    {
        AnalysisResult snapshot = new(source.SourceFile, source.Text);
        snapshot.Objects.AddRange(source.Objects);
        snapshot.Dependencies.AddRange(source.Dependencies);
        snapshot.Parameters.AddRange(source.Parameters);
        snapshot.ResultColumns.AddRange(source.ResultColumns);
        snapshot.TableKeys.AddRange(source.TableKeys);
        snapshot.IndexColumns.AddRange(source.IndexColumns);
        snapshot.ForeignKeys.AddRange(source.ForeignKeys);
        snapshot.Files.AddRange(source.Files);
        snapshot.TableColumns.AddRange(source.TableColumns);
        snapshot.LocalColumns.AddRange(source.LocalColumns);
        snapshot.ParsedBatches.AddRange(source.ParsedBatches);
        snapshot.Problems.AddRange(source.Problems);
        snapshot.LintFindings.AddRange(source.LintFindings);
        return snapshot;
    }

    private IEnumerable<WorkspaceDiagnosticGroup> WorkspaceProblemGroups(
        AnalysisResult loaded,
        IReadOnlySet<string> staleFiles,
        IReadOnlySet<string> openFiles
    )
    {
        return loaded.Problems
            .Where(problem => !openFiles.Contains(problem.SourceFile))
            .GroupBy(problem => problem.SourceFile, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var metadata = DiagnosticMetadataFor(staleFiles.Contains(group.Key) ? "stale-index" : "fresh-index");
                return new WorkspaceDiagnosticGroup(
                    LspUri.FromPath(group.Key),
                    group.DistinctBy(problem => (problem.Line, problem.Column, problem.Message))
                        .Select(problem => (object)new
                        {
                            range = Range(problem.Line, problem.Column, 1),
                            severity = metadata.IsStale ? 3 : 1,
                            source = "sqldeps",
                            code = (string?)null,
                            data = DiagnosticData(metadata),
                            message = problem.Message,
                        }).ToArray());
            });
    }

    private IEnumerable<WorkspaceDiagnosticGroup> WorkspaceLintGroups(
        AnalysisResult loaded,
        IReadOnlySet<string> staleFiles,
        IReadOnlySet<string> openFiles
    )
    {
        return loaded.LintFindings
            .Where(finding => !openFiles.Contains(finding.SourceFile))
            .GroupBy(finding => finding.SourceFile, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var metadata = DiagnosticMetadataFor(staleFiles.Contains(group.Key) ? "stale-index" : "fresh-index", group.FirstOrDefault()?.Rule);
                return new WorkspaceDiagnosticGroup(
                    LspUri.FromPath(group.Key),
                    group.DistinctBy(finding => (finding.Rule, finding.Line, finding.Message))
                        .Select(finding =>
                        {
                            var findingMetadata = DiagnosticMetadataFor(metadata.Source, finding.Rule, finding.Severity);
                            return (object)new
                            {
                                range = Range(finding.Line, finding.Column, finding.Length),
                                severity = findingMetadata.IsStale ? 3 : DiagnosticSeverity(finding.Severity),
                                source = "sqldeps",
                                code = finding.Rule,
                                data = DiagnosticData(findingMetadata),
                                relatedInformation = RelatedInformation(finding),
                                message = finding.Message,
                            };
                        }).ToArray());
            });
    }

    private static HashSet<string> StaleWorkspaceFiles(AnalysisResult loaded)
    {
        return loaded.Files
            .Where(file => !File.Exists(file.Path) || SqlFileReader.HashOf(File.ReadAllBytes(file.Path)) != file.Hash)
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshWorkspaceFile(string path)
    {
        if (!IsWorkspaceSqlFile(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!TryReadWorkspaceFile(fullPath, out var bytes))
            {
                return;
            }

            var refreshed = DocumentAnalysis.Analyze(fullPath, SqlFileReader.Decode(bytes));
            lock (gate)
            {
                if (workspace is null)
                {
                    return;
                }

                ReplaceWorkspaceFile(workspace, refreshed, fullPath, SqlFileReader.HashOf(bytes));
                InvalidateWorkspaceMetadataSnapshotLocked();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: workspace file refresh failed: " + exception.Message);
        }
    }

    private static bool TryReadWorkspaceFile(string path, out byte[] bytes)
    {
        bytes = File.ReadAllBytes(path);

        if (bytes.LongLength > MaxDocumentBytes)
        {
            bytes = [];

            return false;
        }

        return true;
    }

    private static void ReplaceWorkspaceFile(
        AnalysisResult workspace,
        AnalysisResult refreshed,
        string path,
        string hash)
    {
        RemoveWorkspaceFile(workspace, path);
        AddWorkspaceFile(workspace, refreshed, path, hash);
    }

    private static void RemoveWorkspaceFile(AnalysisResult workspace, string path)
    {
        workspace.Objects.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.Dependencies.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.Parameters.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.ResultColumns.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.TableKeys.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.IndexColumns.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.ForeignKeys.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.TableColumns.RemoveAll(item => SamePath(item.SourceFile, path));
        workspace.Files.RemoveAll(item => SamePath(item.Path, path));
    }

    private static void AddWorkspaceFile(
        AnalysisResult workspace,
        AnalysisResult refreshed,
        string path,
        string hash)
    {
        workspace.Objects.AddRange(refreshed.Objects);
        workspace.Dependencies.AddRange(refreshed.Dependencies);
        workspace.Parameters.AddRange(refreshed.Parameters);
        workspace.ResultColumns.AddRange(refreshed.ResultColumns);
        workspace.TableKeys.AddRange(refreshed.TableKeys);
        workspace.IndexColumns.AddRange(refreshed.IndexColumns);
        workspace.ForeignKeys.AddRange(refreshed.ForeignKeys);
        workspace.TableColumns.AddRange(refreshed.TableColumns);
        workspace.Files.Add(new IndexedFile(path, hash));
    }
}
