using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Runs document diagnostics through the public Analysis boundary.</summary>
public static class DocumentDiagnosticAnalyzer
{
    /// <summary>Analyzes a document against indexed metadata without changing the index.</summary>
    public static DocumentDiagnosticAnalysis Analyze(
        string sourceFile,
        string text,
        MetadataCatalog metadata,
        IEnumerable<DatabaseObject> objects,
        IEnumerable<ObjectParameter> parameters,
        string defaultSchema,
        string? repositoryRoot = null,
        string? defaultDatabase = null
    )
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(defaultSchema);

        AnalysisResult analysis = DocumentAnalysis.Analyze(
            sourceFile,
            text,
            defaultSchema,
            defaultDatabase,
            repositoryRoot);
        IEnumerable<LintFinding> findings = analysis.LintFindings
            .Concat(QueryColumnValidator.Validate(
                sourceFile,
                analysis.ParsedBatches,
                metadata,
                analysis.LocalColumns,
                text,
                repositoryRoot,
                defaultSchema,
                defaultDatabase))
            .Concat(ProcedureParameterValidator.Validate(
                sourceFile,
                analysis.ParsedBatches,
                objects,
                parameters,
                text,
                repositoryRoot,
                defaultSchema,
                defaultDatabase))
            .DistinctBy(finding => (finding.Rule, finding.Line, finding.Message));

        return new DocumentDiagnosticAnalysis(
            findings.ToArray(),
            analysis.Problems.ToArray());
    }
}

/// <summary>Diagnostics and parse problems produced for one analyzed SQL document.</summary>
public sealed record DocumentDiagnosticAnalysis(
    IReadOnlyList<LintFinding> Diagnostics,
    IReadOnlyList<ParseProblem> Problems
);
