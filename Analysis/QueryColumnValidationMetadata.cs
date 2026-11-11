using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Pre-indexed column metadata consumed by the query-column visitor.</summary>
internal sealed record QueryColumnValidationMetadata(
    IReadOnlyDictionary<string, HashSet<string>> ColumnsByTable,
    IReadOnlyDictionary<string, TableColumn[]> MetadataByTable,
    IReadOnlyDictionary<string, ResultColumn[]> ResultColumnsByObject,
    IReadOnlyDictionary<string, HashSet<string>> IndexedColumnsByTable,
    IReadOnlyDictionary<string, TableIndexColumn[]> FilteredIndexColumnsByTable,
    IReadOnlyDictionary<(string TableName, string ColumnName), TableForeignKey> ForeignKeysByColumn,
    IReadOnlyDictionary<string, string[]> TablesByBaseName)
{
    public static QueryColumnValidationMetadata Create(MetadataCatalog metadata)
    {
        var tableMetadata = CreateTableMetadata(metadata.TableColumns);
        var resultColumnsByObject = CreateResultColumnMetadata(metadata.ResultColumns);
        var indexMetadata = CreateIndexMetadata(metadata.IndexColumns, metadata.TableKeys);
        var foreignKeysByColumn = CreateForeignKeyMetadata(metadata.ForeignKeys);

        return new(
            tableMetadata.ColumnsByTable,
            tableMetadata.MetadataByTable,
            resultColumnsByObject,
            indexMetadata.IndexedColumnsByTable,
            indexMetadata.FilteredIndexColumnsByTable,
            foreignKeysByColumn,
            tableMetadata.TablesByBaseName);
    }

    private static (
        Dictionary<string, HashSet<string>> ColumnsByTable,
        Dictionary<string, TableColumn[]> MetadataByTable,
        Dictionary<string, string[]> TablesByBaseName) CreateTableMetadata(
        IReadOnlyList<TableColumn> columns)
    {
        Dictionary<string, TableColumn[]> metadataByTable = columns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, HashSet<string>> columnsByTable = metadataByTable
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(column => column.ColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]> tablesByBaseName = metadataByTable.Keys
            .GroupBy(ObjectNames.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return (columnsByTable, metadataByTable, tablesByBaseName);
    }

    private static Dictionary<string, ResultColumn[]> CreateResultColumnMetadata(
        IReadOnlyList<ResultColumn> columns)
    {
        return columns
            .GroupBy(column => column.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(column => column.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static (
        Dictionary<string, HashSet<string>> IndexedColumnsByTable,
        Dictionary<string, TableIndexColumn[]> FilteredIndexColumnsByTable) CreateIndexMetadata(
        IReadOnlyList<TableIndexColumn> indexColumns,
        IReadOnlyList<TableKey> tableKeys)
    {
        var indexedColumnsByTable = indexColumns
            .Where(column => !column.IsIncluded && !column.IsFiltered)
            .GroupBy(column => column.TableName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var filteredIndexColumnsByTable = indexColumns
            .Where(column => !column.IsIncluded && column.IsFiltered && column.FilterPredicate is not null)
            .GroupBy(column => column.TableName)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var key in tableKeys)
        {
            indexedColumnsByTable.GetValueOrDefault(key.TableName)?.Add(key.ColumnName);
        }

        return (indexedColumnsByTable, filteredIndexColumnsByTable);
    }

    private static Dictionary<(string TableName, string ColumnName), TableForeignKey> CreateForeignKeyMetadata(
        IReadOnlyList<TableForeignKey> foreignKeys)
    {
        return foreignKeys
            .GroupBy(key => (key.TableName, key.ColumnName))
            .ToDictionary(group => group.Key, group => group.First());
    }
}
