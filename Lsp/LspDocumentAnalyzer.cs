using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static class LspDocumentAnalyzer
{
    internal static AnalysisResult Analyze(
        string sourceFile,
        string text,
        int maxDocumentCharacters,
        string defaultSchema = ObjectNames.StandardDefaultSchema,
        string? defaultDatabase = null,
        string? repositoryRoot = null
    )
    {
        if (text.Length <= maxDocumentCharacters)
        {
            return DocumentAnalysis.Analyze(sourceFile, text, defaultSchema, defaultDatabase, repositoryRoot);
        }

        return DocumentAnalysis.AnalyzeLimited(
            sourceFile,
            text,
            maxDocumentCharacters,
            defaultSchema,
            defaultDatabase,
            repositoryRoot: repositoryRoot);
    }
}
