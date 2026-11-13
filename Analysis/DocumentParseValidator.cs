using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Exposes parse validation without leaking the internal analysis snapshot.</summary>
public static class DocumentParseValidator
{
    /// <summary>Returns parse problems for a document without changing files or index state.</summary>
    public static IReadOnlyList<ParseProblem> Problems(
        string sourceFile,
        string text,
        string defaultSchema = ObjectNames.StandardDefaultSchema,
        string? repositoryRoot = null
    )
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(defaultSchema);

        return DocumentAnalysis.Analyze(
            sourceFile,
            text,
            defaultSchema,
            repositoryRoot: repositoryRoot).Problems.ToArray();
    }

    /// <summary>Returns whether every batch in the document parses successfully.</summary>
    public static bool IsValid(
        string sourceFile,
        string text,
        string defaultSchema = ObjectNames.StandardDefaultSchema,
        string? repositoryRoot = null
    )
    {
        return Problems(sourceFile, text, defaultSchema, repositoryRoot).Count == 0;
    }
}
