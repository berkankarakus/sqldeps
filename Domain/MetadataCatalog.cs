namespace SqlDeps;

/// <summary>
/// Immutable snapshot of schema and result-set metadata used by analysis boundaries.
/// Keeping the related models together prevents callers from accidentally mixing
/// metadata from different workspace snapshots.
/// </summary>
public sealed class MetadataCatalog
{
    /// <summary>Copies the metadata collections so an analysis snapshot cannot change underneath a visitor.</summary>
    public MetadataCatalog(
        IEnumerable<ResultColumn> resultColumns,
        IEnumerable<TableColumn> tableColumns,
        IEnumerable<TableKey> tableKeys,
        IEnumerable<TableForeignKey> foreignKeys,
        IEnumerable<TableIndexColumn> indexColumns)
    {
        ArgumentNullException.ThrowIfNull(resultColumns);
        ArgumentNullException.ThrowIfNull(tableColumns);
        ArgumentNullException.ThrowIfNull(tableKeys);
        ArgumentNullException.ThrowIfNull(foreignKeys);
        ArgumentNullException.ThrowIfNull(indexColumns);

        ResultColumns = resultColumns.ToArray();
        TableColumns = tableColumns.ToArray();
        TableKeys = tableKeys.ToArray();
        ForeignKeys = foreignKeys.ToArray();
        IndexColumns = indexColumns.ToArray();
    }

    /// <summary>Known procedure and function result columns.</summary>
    public IReadOnlyList<ResultColumn> ResultColumns { get; }
    /// <summary>Columns declared by indexed tables, views, and local SQL objects.</summary>
    public IReadOnlyList<TableColumn> TableColumns { get; }
    /// <summary>Primary and unique key columns.</summary>
    public IReadOnlyList<TableKey> TableKeys { get; }
    /// <summary>Foreign-key relationships.</summary>
    public IReadOnlyList<TableForeignKey> ForeignKeys { get; }
    /// <summary>Known key and included index columns.</summary>
    public IReadOnlyList<TableIndexColumn> IndexColumns { get; }

    /// <summary>Returns a new snapshot containing the metadata from both catalogs.</summary>
    public MetadataCatalog Merge(MetadataCatalog other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new MetadataCatalog(
            ResultColumns.Concat(other.ResultColumns),
            TableColumns.Concat(other.TableColumns),
            TableKeys.Concat(other.TableKeys),
            ForeignKeys.Concat(other.ForeignKeys),
            IndexColumns.Concat(other.IndexColumns));
    }
}
