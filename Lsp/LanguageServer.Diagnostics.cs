using System.Text.Json;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void PublishDiagnostics(object diagnostics)
    {
        if (clientSupportsPullDiagnostics)
        {
            return;
        }

        connection.Notify("textDocument/publishDiagnostics", diagnostics);
    }

    private static int? DocumentVersion(JsonElement document)
    {
        return LspDocumentVersion.Read(document);
    }

    private bool IsOlderDocumentVersion(string uri, int? version)
    {
        lock (gate)
        {
            return IsOlderDocumentVersionLocked(uri, version);
        }
    }

    private bool IsOlderDocumentVersionLocked(string uri, int? version)
    {
        return openDocuments.IsStale(uri, version, allowSameVersion: false);
    }

    private object DocumentDiagnostics(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var analysis = OpenDocument(uri);

        return new
        {
            kind = "full",
            items = analysis is null ? Array.Empty<object>() : DiagnosticsForDocument(uri, analysis, "live-document"),
        };
    }

    private object[] DiagnosticsForDocument(string uri, AnalysisResult analysis, string source)
    {
        int documentRevision;
        int workspaceRevision;

        lock (gate)
        {
            documentRevision = openDocuments.Revision(uri);
            workspaceRevision = workspaceMetadataRevision;

            if (diagnosticCache.TryGetValue(uri, out var cached)
                && IsCurrentDiagnostics(cached, documentRevision, workspaceRevision, source))
            {
                return cached.Items;
            }
        }

        object[] diagnostics = DiagnosticsFor(analysis, source);

        lock (gate)
        {
            if (IsCurrentDocument(uri, documentRevision, workspaceRevision))
            {
                diagnosticCache[uri] = new(documentRevision, workspaceRevision, source, diagnostics);
            }
        }

        return diagnostics;
    }

    private static bool IsCurrentDiagnostics(
        CachedDiagnostics cached,
        int documentRevision,
        int workspaceRevision,
        string source)
    {
        return cached.DocumentRevision == documentRevision
            && cached.WorkspaceRevision == workspaceRevision
            && string.Equals(cached.Source, source, StringComparison.Ordinal);
    }

    private bool IsCurrentDocument(string uri, int documentRevision, int workspaceRevision)
    {
        return openDocuments.Revision(uri) == documentRevision
            && workspaceMetadataRevision == workspaceRevision;
    }

    private object[] DiagnosticsFor(AnalysisResult analysis, string source)
    {
        var problemMetadata = DiagnosticMetadataFor(source, "parse-error", "error");
        var partialDiagnostics = PartialDiagnosticsFor(analysis, source);
        var problems = analysis.Problems
            .DistinctBy(problem => (problem.Line, problem.Column, problem.Message))
            .Select(problem => (object)new
            {
                range = Range(problem.Line, problem.Column, 1),
                severity = 1,
                source = "sqldeps",
                code = (string?)null,
                data = DiagnosticData(problemMetadata),
                message = problem.Message,
            });
        var findings = DistinctLintFindings(analysis.LintFindings)
            .Select(finding =>
            {
                var metadata = DiagnosticMetadataFor(source, finding.Rule, finding.Severity);
                return (object)new
                {
                    range = Range(finding.Line, finding.Column, finding.Length),
                    severity = DiagnosticSeverity(finding.Severity, metadata.IsStale),
                    source = "sqldeps",
                    code = (string?)finding.Rule,
                    data = DiagnosticData(metadata, finding.Rule),
                    relatedInformation = RelatedInformation(finding),
                    message = finding.Message,
                };
            });

        return partialDiagnostics.Concat(problems).Concat(findings).ToArray();
    }

    private static IEnumerable<object> PartialDiagnosticsFor(AnalysisResult analysis, string source)
    {
        if (!analysis.IsPartial)
        {
            return [];
        }

        var metadata = DiagnosticMetadataFor(source, "partial-analysis", "warning");
        var message = analysis.PartialReason ?? "Only part of this SQL document was analyzed.";

        return
        [
            new
            {
                range = new LspRange(new(0, 0), new(0, 1)),
                severity = DiagnosticSeverity("warning"),
                source = "sqldeps",
                code = "partial-analysis",
                data = DiagnosticData(metadata),
                message,
            },
        ];
    }

    private static IEnumerable<LintFinding> DistinctLintFindings(IEnumerable<LintFinding> findings)
    {
        return findings
            .DistinctBy(finding => (finding.Rule, finding.Line, finding.Column, finding.Length, finding.Message));
    }

    private object[] RelatedInformation(LintFinding finding)
    {
        var definition = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, finding.SourceObject));
        if (definition.Object is null)
        {
            return [];
        }

        if (SamePath(finding.SourceFile, definition.Object.SourceFile))
        {
            return [];
        }

        return
        [
            new
            {
                location = Location(definition.Object.SourceFile, definition.Object.Line),
                message = $"Source object '{definition.Object.Name}' is defined here.",
            },
        ];
    }

    private static AnalysisResult AnalyzeDocument(string sourceFile, string text, string? repositoryRoot = null)
    {
        if (text.Length <= MaxDocumentCharacters)
        {
            return DocumentAnalysis.Analyze(sourceFile, text, repositoryRoot: repositoryRoot);
        }

        return DocumentAnalysis.AnalyzeLimited(
            sourceFile,
            text,
            MaxDocumentCharacters,
            repositoryRoot: repositoryRoot);
    }
}
