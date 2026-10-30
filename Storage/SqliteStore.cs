using Microsoft.Data.Sqlite;
using SqlDeps.Analysis;

namespace SqlDeps;

/// <summary>Writes the index to a SQLite file. Names compare without regard to case, as SQL Server's do.</summary>
public static class SqliteStore
{
    private const string Schema = """
        CREATE TABLE objects (
            name        TEXT NOT NULL COLLATE NOCASE,
            kind        TEXT NOT NULL,
            source_file TEXT NOT NULL,
            line        INTEGER NOT NULL
        );

        CREATE TABLE dependencies (
            source_object TEXT NOT NULL COLLATE NOCASE,
            target_object TEXT NOT NULL COLLATE NOCASE,
            operation     TEXT NOT NULL,
            source_file   TEXT NOT NULL,
            line          INTEGER NOT NULL,
            via_dynamic   INTEGER NOT NULL
        );

        CREATE TABLE parameters (
            object_name  TEXT NOT NULL COLLATE NOCASE,
            ordinal      INTEGER NOT NULL,
            name         TEXT NOT NULL,
            data_type    TEXT NOT NULL,
            default_value TEXT,
            is_output    INTEGER NOT NULL,
            source_file  TEXT NOT NULL
        );

        CREATE TABLE column_usage (
            source_object TEXT NOT NULL COLLATE NOCASE,
            table_name    TEXT NOT NULL COLLATE NOCASE,
            column_name   TEXT NOT NULL COLLATE NOCASE,
            operation     TEXT NOT NULL,
            source_file   TEXT NOT NULL,
            line          INTEGER NOT NULL
        );

        CREATE TABLE table_columns (
            table_name  TEXT NOT NULL COLLATE NOCASE,
            column_name TEXT NOT NULL COLLATE NOCASE,
            data_type   TEXT NOT NULL,
            is_nullable INTEGER,
            is_identity INTEGER NOT NULL,
            default_value TEXT,
            is_computed INTEGER NOT NULL,
            precision INTEGER,
            scale INTEGER,
            collation TEXT,
            source_file TEXT NOT NULL,
            line        INTEGER NOT NULL
        );

        CREATE TABLE parse_errors (
            source_file TEXT NOT NULL,
            line        INTEGER NOT NULL,
            message     TEXT NOT NULL
        );

        CREATE TABLE parse_warnings (
            source_file TEXT NOT NULL,
            line        INTEGER NOT NULL,
            message     TEXT NOT NULL
        );

        CREATE TABLE lint_findings (
            rule          TEXT NOT NULL,
            severity      TEXT NOT NULL,
            source_object TEXT NOT NULL COLLATE NOCASE,
            source_file   TEXT NOT NULL,
            line          INTEGER NOT NULL,
            column        INTEGER NOT NULL,
            length        INTEGER NOT NULL,
            message       TEXT NOT NULL
        );

        CREATE TABLE files (
            path   TEXT NOT NULL,
            sha256 TEXT NOT NULL
        );

        CREATE TABLE meta (
            key   TEXT NOT NULL,
            value TEXT NOT NULL
        );

        CREATE TABLE reach (
            source_object  TEXT NOT NULL COLLATE NOCASE,
            reached_object TEXT NOT NULL COLLATE NOCASE,
            depth          INTEGER NOT NULL,
            path           TEXT NOT NULL
        );

        CREATE TABLE cycles (
            object_name TEXT NOT NULL COLLATE NOCASE,
            depth       INTEGER NOT NULL,
            path        TEXT NOT NULL
        );

        CREATE INDEX reach_by_source ON reach (source_object);
        CREATE INDEX reach_by_reached ON reach (reached_object);

        -- Who reads and who writes: the dependencies filtered by what they do.
        CREATE VIEW writers AS
        SELECT * FROM dependencies
        WHERE operation IN ('INSERT', 'UPDATE', 'DELETE', 'MERGE', 'TRUNCATE');

        CREATE VIEW readers AS
        SELECT * FROM dependencies WHERE operation = 'SELECT';

        CREATE VIEW calls AS
        SELECT * FROM dependencies WHERE operation IN ('EXEC', 'CALL');

        -- What is affected if "origin" changes: the reach table read the other way round.
        -- "path" runs from the affected object down to the origin.
        CREATE VIEW impact AS
        SELECT reached_object AS origin, source_object AS affected, depth, path
        FROM reach;

        -- Used but never defined in the indexed files: a missing script, a system object or another database.
        CREATE VIEW undefined_objects AS
        SELECT target_object AS name,
               COUNT(*) AS uses,
               COUNT(DISTINCT source_object) AS used_by,
               MIN(source_file) AS first_file
        FROM dependencies
        WHERE operation <> 'EXEC_DYNAMIC'
          AND target_object NOT IN (SELECT name FROM objects)
        GROUP BY target_object;

        -- Defined but not used by any other object. Triggers are left out (nothing calls them), and
        -- anything called only from application code or a job shows up here too.
        CREATE VIEW unused_objects AS
        SELECT name, kind, source_file, line
        FROM objects
        WHERE kind <> 'trigger'
          AND name NOT IN (SELECT target_object FROM dependencies WHERE source_object <> target_object);

        CREATE VIEW most_written AS
        SELECT target_object AS name,
               COUNT(DISTINCT source_object) AS writers,
               group_concat(DISTINCT operation) AS operations
        FROM dependencies
        WHERE operation IN ('INSERT', 'UPDATE', 'DELETE', 'MERGE', 'TRUNCATE')
        GROUP BY target_object
        ORDER BY writers DESC;

        -- How much an object depends on, and how many other objects depend on it.
        CREATE VIEW object_degree AS
        SELECT o.name,
               o.kind,
               (SELECT COUNT(DISTINCT d.target_object) FROM dependencies d
                 WHERE d.source_object = o.name AND d.operation <> 'EXEC_DYNAMIC') AS depends_on,
               (SELECT COUNT(DISTINCT d.source_object) FROM dependencies d
                 WHERE d.target_object = o.name AND d.source_object <> o.name) AS used_by
        FROM objects o;

        -- The same object created in several files (a CREATE followed by an ALTER in a later script counts too).
        CREATE VIEW duplicate_definitions AS
        SELECT name, kind, COUNT(*) AS definitions, group_concat(source_file || ':' || line, ', ') AS places
        FROM objects
        GROUP BY name, kind
        HAVING COUNT(*) > 1;

        CREATE INDEX dependencies_by_source ON dependencies (source_object);
        CREATE INDEX dependencies_by_target ON dependencies (target_object, operation);
        CREATE INDEX objects_by_name ON objects (name);
        CREATE INDEX column_usage_by_source ON column_usage (source_object, source_file, line);
        CREATE INDEX column_usage_by_table_column ON column_usage (table_name, column_name, operation, source_object, source_file, line);
        CREATE INDEX table_columns_by_table ON table_columns (table_name, column_name);
        CREATE INDEX lint_findings_by_severity ON lint_findings (severity, rule, source_object, source_file, line, column);
        """;

    public static void Write(
        string databasePath,
        IReadOnlyList<DatabaseObject> objects,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<ObjectParameter> parameters,
        IReadOnlyList<ColumnUsage> columnUsage,
        IReadOnlyList<TableColumn> tableColumns,
        IReadOnlyList<ParseProblem> problems,
        IReadOnlyList<IndexWarning> warnings,
        IReadOnlyList<LintFinding> lintFindings,
        IReadOnlyList<IndexedFile> files,
        ReachResult reachability,
        string settings,
        string defaultSchema,
        string sourceRoot
    )
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), "." + Path.GetFileName(databasePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteNew(temporaryPath, objects, dependencies, parameters, columnUsage, tableColumns, problems, warnings, lintFindings, files, reachability, settings, defaultSchema, sourceRoot);
            File.Move(temporaryPath, databasePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteNew(
        string databasePath,
        IReadOnlyList<DatabaseObject> objects,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<ObjectParameter> parameters,
        IReadOnlyList<ColumnUsage> columnUsage,
        IReadOnlyList<TableColumn> tableColumns,
        IReadOnlyList<ParseProblem> problems,
        IReadOnlyList<IndexWarning> warnings,
        IReadOnlyList<LintFinding> lintFindings,
        IReadOnlyList<IndexedFile> files,
        ReachResult reachability,
        string settings,
        string defaultSchema,
        string sourceRoot
    )
    {
        using SqliteConnection connection = new("Data Source=" + databasePath);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using BatchInserter inserts = new(connection, transaction);

        CreateSchema(connection);
        InsertMetadata(inserts, settings, defaultSchema, sourceRoot);
        InsertFiles(inserts, files);

        InsertObjects(inserts, objects);

        InsertDependencies(inserts, dependencies);

        InsertParameters(inserts, parameters);

        InsertColumnUsage(inserts, columnUsage);
        InsertTableColumns(inserts, tableColumns);

        InsertDiagnostics(inserts, problems, warnings, lintFindings);

        InsertReachability(inserts, reachability);

        transaction.Commit();
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
    }

    private static void InsertMetadata(BatchInserter inserts, string settings, string defaultSchema, string sourceRoot)
    {
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "settings", settings);
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "default_schema", defaultSchema);
        inserts.Insert("INSERT INTO meta (key, value) VALUES ($a, $b)", "source_root", sourceRoot);
    }

    private static void InsertFiles(BatchInserter inserts, IReadOnlyList<IndexedFile> files)
    {
        foreach (var item in files)
        {
            inserts.Insert("INSERT INTO files (path, sha256) VALUES ($a, $b)", item.Path, item.Hash);
        }
    }

    private static void InsertObjects(BatchInserter inserts, IReadOnlyList<DatabaseObject> objects)
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

    private static void InsertDependencies(BatchInserter inserts, IReadOnlyList<Dependency> dependencies)
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

    private static void InsertParameters(BatchInserter inserts, IReadOnlyList<ObjectParameter> parameters)
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

    private static void InsertColumnUsage(BatchInserter inserts, IReadOnlyList<ColumnUsage> columnUsage)
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

    private static void InsertTableColumns(BatchInserter inserts, IReadOnlyList<TableColumn> tableColumns)
    {
        foreach (var item in tableColumns)
        {
            inserts.Insert(
                "INSERT INTO table_columns (table_name, column_name, data_type, is_nullable, is_identity, default_value, is_computed, precision, scale, collation, source_file, line) VALUES ($a, $b, $c, $d, $e, $f, $g, $h, $i, $j, $k, $l)",
                item.TableName,
                item.ColumnName,
                item.DataType,
                item.IsNullable is null ? null : item.IsNullable.Value ? 1 : 0,
                item.IsIdentity ? 1 : 0,
                item.DefaultValue,
                item.IsComputed ? 1 : 0,
                item.Precision,
                item.Scale,
                item.Collation,
                item.SourceFile,
                item.Line
            );
        }
    }

    private static void InsertDiagnostics(
        BatchInserter inserts,
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

    private static void InsertReachability(BatchInserter inserts, ReachResult reachability)
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

    private sealed class BatchInserter(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
    {
        private static readonly string[] ParameterNames = ["$a", "$b", "$c", "$d", "$e", "$f", "$g", "$h", "$i", "$j", "$k", "$l"];
        private readonly Dictionary<string, SqliteCommand> commands = new(StringComparer.Ordinal);

        public void Insert(string sql, params object?[] values)
        {
            if (!commands.TryGetValue(sql, out var command))
            {
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;

                for (var index = 0; index < ParameterNames.Length; index++)
                {
                    command.Parameters.Add(new SqliteParameter(ParameterNames[index], DBNull.Value));
                }

                command.Prepare();
                commands.Add(sql, command);
            }

            for (var index = 0; index < values.Length; index++)
            {
                command.Parameters[index].Value = values[index] ?? DBNull.Value;
            }

            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            foreach (var command in commands.Values)
            {
                command.Dispose();
            }
        }
    }
}
