namespace SqlDeps.Indexing;

/// <summary>Progress state emitted while an index is being built.</summary>
public readonly record struct IndexProgress(
    string Phase,
    int Completed,
    int Total,
    int ReusedFiles
);
