using SqlDeps.Analysis;

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
        AnalysisResult analysis = DocumentAnalysis.Analyze(
            sourceFile,
            text,
            defaultSchema,
            repositoryRoot);
        IEnumerable<LintFinding> findings = analysis.LintFindings
            .Concat(QueryColumnValidator.Validate(
                sourceFile,
                analysis.ParsedBatches,
                indexed.Catalog,
                analysis.LocalColumns,
                text,
                repositoryRoot))
            .Concat(ProcedureParameterValidator.Validate(
                sourceFile,
                analysis.ParsedBatches,
                indexed.Objects,
                indexed.Parameters,
                text,
                repositoryRoot))
            .DistinctBy(finding => (finding.Rule, finding.Line, finding.Message));

        return new DocumentDiagnosticResult(
            findings.ToArray(),
            analysis.Problems.ToArray());
    }
}

/// <summary>Static diagnostics and parse problems for an analyzed SQL document.</summary>
public sealed record DocumentDiagnosticResult(
    IReadOnlyList<LintFinding> Diagnostics,
    IReadOnlyList<ParseProblem> Problems);
