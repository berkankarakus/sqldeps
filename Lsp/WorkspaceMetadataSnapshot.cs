using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Combined workspace and open-document metadata for one LSP state revision.</summary>
internal sealed class WorkspaceMetadataSnapshot(
    int revision,
    (DatabaseObject Object, string Uri)[] objects,
    Dependency[] dependencies,
    ColumnUsage[] columnUsage,
    ObjectParameter[] parameters,
    TableColumn[] tableColumns,
    ResultColumn[] resultColumns,
    TableKey[] tableKeys,
    TableIndexColumn[] indexColumns,
    TableForeignKey[] foreignKeys
)
{
    public int Revision { get; } = revision;
    public (DatabaseObject Object, string Uri)[] Objects { get; } = objects;
    public Dependency[] Dependencies { get; } = dependencies;
    public ColumnUsage[] ColumnUsage { get; } = columnUsage;
    public ObjectParameter[] Parameters { get; } = parameters;
    public TableColumn[] TableColumns { get; } = tableColumns;
    public ResultColumn[] ResultColumns { get; } = resultColumns;
    public TableKey[] TableKeys { get; } = tableKeys;
    public TableIndexColumn[] IndexColumns { get; } = indexColumns;
    public TableForeignKey[] ForeignKeys { get; } = foreignKeys;
}
