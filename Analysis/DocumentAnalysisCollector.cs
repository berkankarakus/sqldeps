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
        DependencyCollector collector = new(
            sourceFile,
            startLine,
            CteNameCollector.Collect(script),
            defaultSchema,
            text);
        collector.Collect(script);

        result.AppendDependencyContribution(
            collector.CreateContribution(),
            text,
            startLine,
            fullDocumentText,
            repositoryRoot);
        result.LocalColumns.AddRange(LocalColumnCollector.Collect(script, startLine));
    }
}
