using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Collects dependency metadata and metrics through the public Analysis boundary.</summary>
public static class DependencyCollectionAnalyzer
{
    /// <summary>Collects one parsed batch without changing files or index state.</summary>
    public static DependencyCollectionAnalysis Collect(
        TSqlScript script,
        string sourceFile,
        int startLine,
        string defaultSchema,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(defaultSchema);
        ArgumentNullException.ThrowIfNull(sourceText);

        DependencyCollectionSnapshot snapshot = DependencyContributionCollector.Collect(
            script,
            sourceFile,
            startLine,
            defaultSchema,
            sourceText);

        return new DependencyCollectionAnalysis(
            snapshot.Contribution,
            snapshot.GenerateScriptTicks,
            snapshot.GenerateScriptCalls,
            snapshot.SourceSliceHits);
    }
}

/// <summary>Dependency metadata and collection metrics produced for one parsed batch.</summary>
public sealed record DependencyCollectionAnalysis(
    DependencyCollectionResult Contribution,
    long GenerateScriptTicks,
    int GenerateScriptCalls,
    int SourceSliceHits);
