using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Core;

/// <summary>Runs the shared static analysis pipeline for one unsaved SQL document.</summary>
public static class DocumentDiagnosticService
{
    /// <summary>Analyzes a document against indexed metadata without writing files or changing the index.</summary>
    public static DocumentDiagnosticResult Analyze(
        string sourceFile,
        string text,
        IndexAnalysisMetadata indexed,
        string defaultSchema,
        string? repositoryRoot = null)
    {
        DocumentDiagnosticAnalysis analysis = DocumentDiagnosticAnalyzer.Analyze(
            sourceFile,
            text,
            indexed.Catalog,
            indexed.Objects,
            indexed.Parameters,
            defaultSchema,
            repositoryRoot);

        return new DocumentDiagnosticResult(
            analysis.Diagnostics,
            analysis.Problems);
    }
}

/// <summary>Static diagnostics and parse problems for an analyzed SQL document.</summary>
public sealed record DocumentDiagnosticResult(
    IReadOnlyList<LintFinding> Diagnostics,
    IReadOnlyList<ParseProblem> Problems);
