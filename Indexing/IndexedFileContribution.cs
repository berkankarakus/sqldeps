using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>All metadata contributed by one SQL file before the folder index merges it.</summary>
internal sealed class IndexedFileContribution(IndexedFile file)
{
    internal IndexedFile File { get; } = file;
    internal bool WasReused { get; private set; }
    internal long ParseTicks { get; set; }
    internal long CollectTicks { get; set; }
    internal long GenerateScriptTicks { get; set; }
    internal int GenerateScriptCalls { get; set; }
    internal int SourceSliceHits { get; set; }
    internal int ReusedBatchCount { get; set; }
    internal int ParsedBatchCount { get; set; }
    internal List<DatabaseObject> Objects { get; } = [];
    internal List<Dependency> Dependencies { get; } = [];
    internal List<ObjectParameter> Parameters { get; } = [];
    internal List<ResultColumn> ResultColumns { get; } = [];
    internal List<TableKey> TableKeys { get; } = [];
    internal List<TableForeignKey> ForeignKeys { get; } = [];
    internal List<TableIndexColumn> IndexColumns { get; } = [];
    internal List<ColumnUsage> ColumnUsage { get; } = [];
    internal List<TableColumn> TableColumns { get; } = [];
    internal List<ParseProblem> Problems { get; } = [];
    internal List<IndexWarning> Warnings { get; } = [];
    internal List<LintFinding> LintFindings { get; } = [];
    internal List<CachedBatch> Batches { get; } = [];

    internal void MarkReused()
    {
        WasReused = true;
    }

    internal void Merge(IndexedFileContribution contribution)
    {
        Objects.AddRange(contribution.Objects);
        Dependencies.AddRange(contribution.Dependencies);
        Parameters.AddRange(contribution.Parameters);
        ResultColumns.AddRange(contribution.ResultColumns);
        TableKeys.AddRange(contribution.TableKeys);
        ForeignKeys.AddRange(contribution.ForeignKeys);
        IndexColumns.AddRange(contribution.IndexColumns);
        ColumnUsage.AddRange(contribution.ColumnUsage);
        TableColumns.AddRange(contribution.TableColumns);
        Problems.AddRange(contribution.Problems);
        Warnings.AddRange(contribution.Warnings);
        LintFindings.AddRange(contribution.LintFindings);
        Batches.AddRange(contribution.Batches);
        ParseTicks += contribution.ParseTicks;
        CollectTicks += contribution.CollectTicks;
        GenerateScriptTicks += contribution.GenerateScriptTicks;
        GenerateScriptCalls += contribution.GenerateScriptCalls;
        SourceSliceHits += contribution.SourceSliceHits;
        ReusedBatchCount += contribution.ReusedBatchCount;
        ParsedBatchCount += contribution.ParsedBatchCount;
    }

    internal static IndexedFileContribution FromCache(IndexedFile file, CachedFile cached)
    {
        var contribution = new IndexedFileContribution(file);
        contribution.Objects.AddRange(cached.Objects);
        contribution.Dependencies.AddRange(cached.Dependencies);
        contribution.Parameters.AddRange(cached.Parameters);
        contribution.ResultColumns.AddRange(cached.ResultColumns);
        contribution.TableKeys.AddRange(cached.TableKeys);
        contribution.ForeignKeys.AddRange(cached.ForeignKeys);
        contribution.IndexColumns.AddRange(cached.IndexColumns);
        contribution.ColumnUsage.AddRange(cached.ColumnUsage);
        contribution.TableColumns.AddRange(cached.TableColumns);
        contribution.Problems.AddRange(cached.Problems);
        contribution.Warnings.AddRange(cached.Warnings);
        contribution.LintFindings.AddRange(cached.LintFindings);
        contribution.MarkReused();
        return contribution;
    }
}
