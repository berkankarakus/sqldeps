using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Adapts indexing visitors to the in-memory document analysis result.</summary>
internal static class DocumentAnalysisCollector
{
    public static void Append(
        TSqlScript script,
        string sourceFile,
        string text,
        int startLine,
        string defaultSchema,
        AnalysisResult result,
        string fullDocumentText,
        string? repositoryRoot)
    {
        DependencyCollectionSnapshot collection = DependencyContributionCollector.Collect(
            script,
            sourceFile,
            startLine,
            defaultSchema,
            text);

        result.AppendDependencyContribution(
            collection.Contribution,
            text,
            startLine,
            fullDocumentText,
            repositoryRoot);
        result.LocalColumns.AddRange(LocalColumnCollector.Collect(script, startLine));
    }
}
