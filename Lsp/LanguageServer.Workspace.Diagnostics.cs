using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void RefreshOpenDocuments()
    {
        OpenDocumentSnapshot[] snapshots;
        lock (gate)
        {
            snapshots = openDocuments.Snapshots().ToArray();
        }

        foreach (var snapshot in snapshots)
        {
            var analysis = AnalyzeDocument(
                snapshot.Analysis.SourceFile,
                snapshot.Analysis.Text,
                workspaceAnalysisRoot);
            AddLiveDocumentFindings(analysis);

            lock (gate)
            {
                if (!openDocuments.ReplaceIfCurrent(snapshot.Uri, analysis, snapshot.Revision))
                {
                    continue;
                }

                InvalidateWorkspaceMetadataSnapshotLocked();
            }

            PublishDiagnostics(new
            {
                uri = snapshot.Uri,
                diagnostics = DiagnosticsForDocument(snapshot.Uri, analysis, "live-document"),
            });
        }
    }

    private void RequestDiagnosticRefresh()
    {
        if (!clientSupportsPullDiagnostics || !clientSupportsDiagnosticRefresh)
        {
            return;
        }

        connection.Request("workspace/diagnostic/refresh", new
        {
        });
    }

    private static string AbsoluteSource(string root, string relativePath) => Path.GetFullPath(Path.Combine(root, relativePath));

    private object WorkspaceDiagnostics()
    {
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
            return new
            {
                items = Array.Empty<object>()
            };
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

        return new
        {
            items = groups.ToArray()
        };
    }

    private static AnalysisResult Snapshot(AnalysisResult source)
    {
        AnalysisResult snapshot = new(source.SourceFile, source.Text);
        snapshot.Objects.AddRange(source.Objects);
        snapshot.Dependencies.AddRange(source.Dependencies);
        snapshot.ColumnUsage.AddRange(source.ColumnUsage);
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
                            severity = metadata.IsStale ? LspDiagnosticSeverityInformation : LspDiagnosticSeverityError,
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
                    DistinctLintFindings(group)
                        .Select(finding =>
                        {
                            var findingMetadata = DiagnosticMetadataFor(metadata.Source, finding.Rule, finding.Severity);
                            return (object)new
                            {
                                range = Range(finding.Line, finding.Column, finding.Length),
                                severity = findingMetadata.IsStale ? LspDiagnosticSeverityInformation : DiagnosticSeverity(finding.Severity),
                                source = "sqldeps",
                                code = finding.Rule,
                                data = DiagnosticData(findingMetadata, finding.Rule),
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
}
