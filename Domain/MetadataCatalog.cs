namespace SqlDeps;

/// <summary>
/// Immutable snapshot of schema and result-set metadata used by analysis boundaries.
/// Keeping the related models together prevents callers from accidentally mixing
/// metadata from different workspace snapshots.
/// </summary>
public sealed class MetadataCatalog
{
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

    public IReadOnlyList<ResultColumn> ResultColumns { get; }
    public IReadOnlyList<TableColumn> TableColumns { get; }
    public IReadOnlyList<TableKey> TableKeys { get; }
    public IReadOnlyList<TableForeignKey> ForeignKeys { get; }
    public IReadOnlyList<TableIndexColumn> IndexColumns { get; }

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
