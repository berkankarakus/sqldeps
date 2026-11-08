using Microsoft.Data.Sqlite;
using SqlDeps.Domain;

namespace SqlDeps.Storage;

/// <summary>Shared SQLite transfer rows for metadata whose schema is wider than its core model constructor.</summary>
internal static class SqliteMetadataMapper
{
    public static ResultColumnRow ToRow(ResultColumn value) => new(
        ObjectName: value.ObjectName,
        Ordinal: value.Ordinal,
        ColumnName: value.ColumnName,
        SourceFile: value.SourceFile,
        Line: value.Line,
        IsKnownName: value.IsKnownName,
        DataType: value.DataType
    );

    public static ResultColumnRow ReadResultColumn(SqliteDataReader reader) => new(
        ObjectName: reader.GetString(0),
        Ordinal: reader.GetInt32(1),
        ColumnName: reader.GetString(2),
        SourceFile: reader.GetString(3),
        Line: reader.GetInt32(4),
        IsKnownName: reader.GetInt32(5) != 0,
        DataType: reader.IsDBNull(6) ? null : reader.GetString(6)
    );

    public static TableIndexColumnRow ToRow(TableIndexColumn value) => new(
        TableName: value.TableName,
        IndexName: value.IndexName,
        ColumnName: value.ColumnName,
        Ordinal: value.Ordinal,
        IsIncluded: value.IsIncluded,
        IsDescending: value.IsDescending,
        IsUnique: value.IsUnique,
        IsClustered: value.IsClustered,
        IsFiltered: value.IsFiltered,
        FilterPredicate: value.FilterPredicate,
        SourceFile: value.SourceFile,
        Line: value.Line
    );

    public static TableIndexColumnRow ReadTableIndexColumn(SqliteDataReader reader) => new(
        TableName: reader.GetString(0),
        IndexName: reader.GetString(1),
        ColumnName: reader.GetString(2),
        Ordinal: reader.GetInt32(3),
        IsIncluded: reader.GetInt32(4) != 0,
        IsDescending: reader.GetInt32(5) != 0,
        IsUnique: reader.GetInt32(6) != 0,
        IsClustered: reader.GetInt32(7) != 0,
        IsFiltered: reader.GetInt32(8) != 0,
        FilterPredicate: reader.IsDBNull(9) ? null : reader.GetString(9),
        SourceFile: reader.GetString(10),
        Line: reader.GetInt32(11)
    );

    public static TableColumnRow ToRow(TableColumn value) => new(
        TableName: value.TableName,
        ColumnName: value.ColumnName,
        DataType: value.DataType,
        SourceFile: value.SourceFile,
        Line: value.Line,
        IsNullable: value.IsNullable,
        IsIdentity: value.IsIdentity,
        DefaultValue: value.DefaultValue,
        IsComputed: value.IsComputed,
        Precision: value.Precision,
        Scale: value.Scale,
        Collation: value.Collation
    );

    public static TableColumnRow ReadTableColumn(SqliteDataReader reader) => new(
        TableName: reader.GetString(0),
        ColumnName: reader.GetString(1),
        DataType: reader.GetString(2),
        IsNullable: reader.IsDBNull(3) ? null : reader.GetInt32(3) != 0,
        IsIdentity: reader.GetInt32(4) != 0,
        DefaultValue: reader.IsDBNull(5) ? null : reader.GetString(5),
        IsComputed: reader.GetInt32(6) != 0,
        Precision: reader.IsDBNull(7) ? null : reader.GetInt32(7),
        Scale: reader.IsDBNull(8) ? null : reader.GetInt32(8),
        Collation: reader.IsDBNull(9) ? null : reader.GetString(9),
        SourceFile: reader.GetString(10),
        Line: reader.GetInt32(11)
    );
}

internal sealed record ResultColumnRow(
    string ObjectName,
    int Ordinal,
    string ColumnName,
    string SourceFile,
    int Line,
    bool IsKnownName,
    string? DataType
)
{
    public ResultColumn ToModel() => new(
        ObjectName: ObjectName,
        Ordinal: Ordinal,
        ColumnName: ColumnName,
        SourceFile: SourceFile,
        Line: Line,
        IsKnownName: IsKnownName,
        DataType: DataType
    );
}

internal sealed record TableIndexColumnRow(
    string TableName,
    string IndexName,
    string ColumnName,
    int Ordinal,
    bool IsIncluded,
    bool IsDescending,
    bool IsUnique,
    bool IsClustered,
    bool IsFiltered,
    string? FilterPredicate,
    string SourceFile,
    int Line
)
{
    public TableIndexColumn ToModel() => new(
        TableName: TableName,
        IndexName: IndexName,
        ColumnName: ColumnName,
        Ordinal: Ordinal,
        IsIncluded: IsIncluded,
        IsDescending: IsDescending,
        IsUnique: IsUnique,
        IsClustered: IsClustered,
        IsFiltered: IsFiltered,
        FilterPredicate: FilterPredicate,
        SourceFile: SourceFile,
        Line: Line
    );
}

internal sealed record TableColumnRow(
    string TableName,
    string ColumnName,
    string DataType,
    string SourceFile,
    int Line,
    bool? IsNullable,
    bool IsIdentity,
    string? DefaultValue,
    bool IsComputed,
    int? Precision,
    int? Scale,
    string? Collation
)
{
    public TableColumn ToModel() => new(
        TableName: TableName,
        ColumnName: ColumnName,
        DataType: DataType,
        SourceFile: SourceFile,
        Line: Line,
        IsNullable: IsNullable,
        IsIdentity: IsIdentity,
        DefaultValue: DefaultValue,
        IsComputed: IsComputed,
        Precision: Precision,
        Scale: Scale,
        Collation: Collation
    );
}
