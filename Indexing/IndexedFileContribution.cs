namespace SqlDeps;

/// <summary>All metadata contributed by one SQL file before the folder index merges it.</summary>
internal sealed class IndexedFileContribution(IndexedFile file)
{
    public IndexedFile File { get; } = file;
    public bool Reused { get; private set; }
    public bool WasReused => Reused;
    public long ParseTicks { get; set; }
    public long CollectTicks { get; set; }
    public long GenerateScriptTicks { get; set; }
    public int GenerateScriptCalls { get; set; }
    public int SourceSliceHits { get; set; }
    public List<DatabaseObject> Objects { get; } = [];
    public List<Dependency> Dependencies { get; } = [];
    public List<ObjectParameter> Parameters { get; } = [];
    public List<ResultColumn> ResultColumns { get; } = [];
    public List<TableKey> TableKeys { get; } = [];
    public List<TableForeignKey> ForeignKeys { get; } = [];
    public List<TableIndexColumn> IndexColumns { get; } = [];
    public List<ColumnUsage> ColumnUsage { get; } = [];
    public List<TableColumn> TableColumns { get; } = [];
    public List<ParseProblem> Problems { get; } = [];
    public List<IndexWarning> Warnings { get; } = [];
    public List<LintFinding> LintFindings { get; } = [];
    public List<CachedBatch> Batches { get; } = [];

    public void MarkReused()
    {
        Reused = true;
    }

    public void Merge(IndexedFileContribution contribution)
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
    }

    public static IndexedFileContribution FromCache(IndexedFile file, CachedFile cached)
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
