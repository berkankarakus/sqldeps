using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

internal static class LspDocumentAnalyzer
{
    public static AnalysisResult Analyze(
        string sourceFile,
        string text,
        int maxDocumentCharacters,
        string? repositoryRoot = null
    )
    {
        if (text.Length <= maxDocumentCharacters)
        {
            return DocumentAnalysis.Analyze(sourceFile, text, repositoryRoot: repositoryRoot);
        }

        return DocumentAnalysis.AnalyzeLimited(
            sourceFile,
            text,
            maxDocumentCharacters,
            repositoryRoot: repositoryRoot);
    }
}
