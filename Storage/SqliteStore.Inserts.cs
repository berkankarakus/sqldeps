using Microsoft.Data.Sqlite;
using SqlDeps.Analysis;
using SqlDeps.Storage;

namespace SqlDeps;

public static partial class SqliteStore
{
    private static void CreateSchema(SqliteConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
    }

    private static void InsertMetadata(SqliteBatchInserter inserts, string settings, string defaultSchema, string sourceRoot)
    {
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "schema_version", SqliteSchema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "settings", settings);
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "default_schema", defaultSchema);
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "source_root", sourceRoot);
    }

    private static void InsertFiles(SqliteBatchInserter inserts, IReadOnlyList<IndexedFile> files)
    {
        foreach (var item in files)
        {
            inserts.Insert("INSERT INTO files (path, sha256) VALUES ($a, $b)", item.Path, item.Hash);
        }
    }

    private static void InsertObjects(SqliteBatchInserter inserts, IReadOnlyList<DatabaseObject> objects)
    {
        foreach (var item in objects)
        {
            inserts.Insert(
                "INSERT INTO objects (name, kind, source_file, line) VALUES ($a, $b, $c, $d)",
                item.Name,
                item.Kind,
                item.SourceFile,
                item.Line
            );
        }
    }

    private static void InsertDependencies(SqliteBatchInserter inserts, IReadOnlyList<Dependency> dependencies)
    {
        foreach (var item in dependencies)
        {
            inserts.Insert(
                "INSERT INTO dependencies (source_object, target_object, operation, source_file, line, via_dynamic) VALUES ($a, $b, $c, $d, $e, $f)",
                item.SourceObject,
                item.TargetObject,
                item.Operation,
                item.SourceFile,
                item.Line,
                item.ViaDynamic ? 1 : 0
            );
        }
    }

    private static void InsertParameters(SqliteBatchInserter inserts, IReadOnlyList<ObjectParameter> parameters)
    {
        foreach (var item in parameters)
        {
            inserts.Insert(
                "INSERT INTO parameters (object_name, ordinal, name, data_type, default_value, is_output, source_file) VALUES ($a, $b, $c, $d, $e, $f, $g)",
                item.ObjectName,
                item.Ordinal,
                item.Name,
                item.DataType,
                item.DefaultValue,
                item.IsOutput ? 1 : 0,
                item.SourceFile
            );
        }
    }

    private static void InsertResultColumns(SqliteBatchInserter inserts, IReadOnlyList<ResultColumn> resultColumns)
    {
        foreach (var item in resultColumns)
        {
            var row = SqliteMetadataMapper.ToRow(item);
            inserts.Insert(
                "INSERT INTO result_columns (object_name, ordinal, column_name, source_file, line, is_known_name, data_type) VALUES ($a, $b, $c, $d, $e, $f, $g)",
                row.ObjectName,
                row.Ordinal,
                row.ColumnName,
                row.SourceFile,
                row.Line,
                row.IsKnownName ? 1 : 0,
                row.DataType
            );
        }
    }

    private static void InsertTableKeys(SqliteBatchInserter inserts, IReadOnlyList<TableKey> tableKeys)
    {
        foreach (var item in tableKeys)
        {
            inserts.Insert(
                "INSERT INTO table_keys (table_name, kind, constraint_name, column_name, ordinal, source_file, line) VALUES ($a, $b, $c, $d, $e, $f, $g)",
                item.TableName,
                item.Kind,
                item.ConstraintName,
                item.ColumnName,
                item.Ordinal,
                item.SourceFile,
                item.Line
            );
        }
    }

    private static void InsertForeignKeys(SqliteBatchInserter inserts, IReadOnlyList<TableForeignKey> foreignKeys)
    {
        foreach (var item in foreignKeys)
        {
            inserts.Insert(
                "INSERT INTO foreign_keys (table_name, constraint_name, column_name, referenced_table_name, referenced_column_name, ordinal, source_file, line) VALUES ($a, $b, $c, $d, $e, $f, $g, $h)",
                item.TableName,
                item.ConstraintName,
                item.ColumnName,
                item.ReferencedTableName,
                item.ReferencedColumnName,
                item.Ordinal,
                item.SourceFile,
                item.Line
            );
        }
    }

    private static void InsertIndexColumns(SqliteBatchInserter inserts, IReadOnlyList<TableIndexColumn> indexColumns)
    {
        foreach (var item in indexColumns)
        {
            var row = SqliteMetadataMapper.ToRow(item);
            inserts.Insert(
                "INSERT INTO index_columns (table_name, index_name, column_name, ordinal, is_included, is_descending, is_unique, is_clustered, is_filtered, filter_predicate, source_file, line) VALUES ($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k, $l)",
                row.TableName,
                row.IndexName,
                row.ColumnName,
                row.Ordinal,
                row.IsIncluded ? 1 : 0,
                row.IsDescending ? 1 : 0,
                row.IsUnique ? 1 : 0,
                row.IsClustered ? 1 : 0,
                row.IsFiltered ? 1 : 0,
                row.FilterPredicate,
                row.SourceFile,
                row.Line
            );
        }
    }

    private static void InsertColumnUsage(SqliteBatchInserter inserts, IReadOnlyList<ColumnUsage> columnUsage)
    {
        foreach (var item in columnUsage)
        {
            inserts.Insert(
                "INSERT INTO column_usage (source_object, table_name, column_name, operation, source_file, line) VALUES ($a, $b, $c, $d, $e, $f)",
                item.SourceObject,
                item.TableName,
                item.ColumnName,
                item.Operation,
                item.SourceFile,
                item.Line
            );
        }
    }

    private static void InsertTableColumns(SqliteBatchInserter inserts, IReadOnlyList<TableColumn> tableColumns)
    {
        foreach (var item in tableColumns)
        {
            var row = SqliteMetadataMapper.ToRow(item);
            inserts.Insert(
                "INSERT INTO table_columns (table_name, column_name, data_type, is_nullable, is_identity, default_value, is_computed, precision, scale, collation, source_file, line) VALUES ($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k, $l)",
                row.TableName,
                row.ColumnName,
                row.DataType,
                row.IsNullable is null ? null : row.IsNullable.Value ? 1 : 0,
                row.IsIdentity ? 1 : 0,
                row.DefaultValue,
                row.IsComputed ? 1 : 0,
                row.Precision,
                row.Scale,
                row.Collation,
                row.SourceFile,
                row.Line
            );
        }
    }

    private static void InsertDiagnostics(
        SqliteBatchInserter inserts,
        IReadOnlyList<ParseProblem> problems,
        IReadOnlyList<IndexWarning> warnings,
        IReadOnlyList<LintFinding> lintFindings
    )
    {
        foreach (var item in problems)
        {
            inserts.Insert(
                "INSERT INTO parse_errors (source_file, line, message) VALUES ($a, $b, $c)",
                item.SourceFile,
                item.Line,
                item.Message
            );
        }

        foreach (var warning in warnings)
        {
            inserts.Insert(
                "INSERT INTO parse_warnings (source_file, line, message) VALUES ($a, $b, $c)",
                warning.SourceFile,
                warning.Line,
                warning.Message
            );
        }

        foreach (var finding in lintFindings)
        {
            inserts.Insert(
                "INSERT INTO lint_findings (rule, severity, source_object, source_file, line, column, length, message) VALUES ($a, $b, $c, $d, $e, $f, $g, $h)",
                finding.Rule,
                finding.Severity,
                finding.SourceObject,
                finding.SourceFile,
                finding.Line,
                finding.Column,
                finding.Length,
                finding.Message
            );
        }
    }

    private static void InsertReachability(SqliteBatchInserter inserts, ReachResult reachability)
    {
        foreach (var item in reachability.Reach)
        {
            inserts.Insert(
                "INSERT INTO reach (source_object, reached_object, depth, path) VALUES ($a, $b, $c, $d)",
                item.Source,
                item.Reached,
                item.Depth,
                item.Path
            );
        }

        foreach (var item in reachability.Cycles)
        {
            inserts.Insert(
                "INSERT INTO cycles (object_name, depth, path) VALUES ($a, $b, $c)",
                item.ObjectName,
                item.Depth,
                item.Path
            );
        }
    }
}
