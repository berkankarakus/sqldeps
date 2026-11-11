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

        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.ColumnUsage.AddRange(collector.ColumnUsage);
        result.Parameters.AddRange(collector.Parameters);
        result.ResultColumns.AddRange(collector.ResultColumns);
        result.TableKeys.AddRange(collector.TableKeys);
        result.IndexColumns.AddRange(collector.IndexColumns);
        result.ForeignKeys.AddRange(collector.ForeignKeys);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(
            collector.LintFindings,
            text,
            startLine,
            fullDocumentText,
            repositoryRoot));
        result.LocalColumns.AddRange(LocalColumnCollector.Collect(script, startLine));
    }
}
