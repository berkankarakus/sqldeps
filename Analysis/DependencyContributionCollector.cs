using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Runs the ScriptDom dependency visitor and exposes its immutable contribution.</summary>
internal static class DependencyContributionCollector
{
    public static DependencyCollectionSnapshot Collect(
        TSqlScript script,
        string sourceFile,
        int startLine,
        string defaultSchema,
        string sourceText)
    {
        DependencyCollector collector = new(
            sourceFile,
            startLine,
            CteNameCollector.Collect(script),
            defaultSchema,
            sourceText);
        collector.Collect(script);

        return new DependencyCollectionSnapshot(
            collector.CreateContribution(),
            collector.GenerateScriptTicks,
            collector.GenerateScriptCalls,
            collector.SourceSliceHits);
    }
}

/// <summary>Dependency metadata and collection metrics produced for one parsed batch.</summary>
internal sealed record DependencyCollectionSnapshot(
    DependencyCollectionResult Contribution,
    long GenerateScriptTicks,
    int GenerateScriptCalls,
    int SourceSliceHits);
