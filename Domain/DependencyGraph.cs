namespace SqlDeps.Domain;

/// <summary>Everything one object reaches, and how: "source reaches reached in depth steps along path".</summary>
/// <param name="Source">The graph source object.</param>
/// <param name="Reached">The object reached from the source.</param>
/// <param name="Depth">The number of dependency edges traversed.</param>
/// <param name="Path">The formatted dependency path.</param>
public sealed record ReachRow(
    string Source,
    string Reached,
    int Depth,
    string Path
);

/// <summary>An object that ends up calling or using itself, with the shortest way round.</summary>
/// <param name="ObjectName">The object participating in the cycle.</param>
/// <param name="Depth">The cycle length in graph edges.</param>
/// <param name="Path">The dependency path that forms the cycle.</param>
public sealed record CycleRow(
    string ObjectName,
    int Depth,
    string Path
);

/// <summary>Contains transitive reachability rows and detected cycles.</summary>
/// <param name="Reach">The shortest paths from each source to each reached object.</param>
/// <param name="Cycles">The detected self-referential paths.</param>
public sealed record ReachResult(
    IReadOnlyList<ReachRow> Reach,
    IReadOnlyList<CycleRow> Cycles
);
