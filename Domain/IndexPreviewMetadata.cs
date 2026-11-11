namespace SqlDeps.Domain;

/// <summary>One indexed table or view name and its stored object kind.</summary>
public sealed record IndexedObjectMetadata(string Name, string Kind);

/// <summary>One single-column foreign-key relation read from an index.</summary>
public sealed record IndexedForeignKey(
    string ColumnName,
    string ReferencedTableName,
    string ReferencedColumnName
);
