using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
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
