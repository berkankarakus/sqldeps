using Microsoft.Data.Sqlite;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Storage;

/// <summary>Writes the index to a SQLite file. Names compare without regard to case, as SQL Server's do.</summary>
public static partial class SqliteStore
{
    /// <summary>Current SQLite schema version written by the indexer.</summary>
    public const int SchemaVersion = 2;

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

        CREATE TABLE result_columns (
            object_name TEXT NOT NULL COLLATE NOCASE,
            ordinal INTEGER NOT NULL,
            column_name TEXT NOT NULL COLLATE NOCASE,
            source_file TEXT NOT NULL,
            line INTEGER NOT NULL,
            is_known_name INTEGER NOT NULL,
            data_type TEXT
        );

        CREATE TABLE table_keys (
            table_name TEXT NOT NULL COLLATE NOCASE,
            kind TEXT NOT NULL,
            constraint_name TEXT NOT NULL COLLATE NOCASE,
            column_name TEXT NOT NULL COLLATE NOCASE,
            ordinal INTEGER NOT NULL,
            source_file TEXT NOT NULL,
            line INTEGER NOT NULL
        );

        CREATE TABLE foreign_keys (
            table_name TEXT NOT NULL COLLATE NOCASE,
            constraint_name TEXT NOT NULL COLLATE NOCASE,
            column_name TEXT NOT NULL COLLATE NOCASE,
            referenced_table_name TEXT NOT NULL COLLATE NOCASE,
            referenced_column_name TEXT NOT NULL COLLATE NOCASE,
            ordinal INTEGER NOT NULL,
            source_file TEXT NOT NULL,
            line INTEGER NOT NULL
        );

        CREATE TABLE index_columns (
            table_name TEXT NOT NULL COLLATE NOCASE,
            index_name TEXT NOT NULL COLLATE NOCASE,
            column_name TEXT NOT NULL COLLATE NOCASE,
            ordinal INTEGER NOT NULL,
            is_included INTEGER NOT NULL,
            is_descending INTEGER NOT NULL,
            is_unique INTEGER NOT NULL,
            is_clustered INTEGER NOT NULL,
            is_filtered INTEGER NOT NULL,
            filter_predicate TEXT,
            source_file TEXT NOT NULL,
            line INTEGER NOT NULL
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

        CREATE TABLE file_batches (
            source_file TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            start_line INTEGER NOT NULL,
            text_hash TEXT NOT NULL,
            payload TEXT NOT NULL
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
        CREATE INDEX file_batches_by_file ON file_batches (source_file, ordinal);
        CREATE INDEX column_usage_by_source ON column_usage (source_object, source_file, line);
        CREATE INDEX column_usage_by_table_column ON column_usage (table_name, column_name, operation, source_object, source_file, line);
        CREATE INDEX table_columns_by_table ON table_columns (table_name, column_name);
        CREATE INDEX lint_findings_by_severity ON lint_findings (severity, rule, source_object, source_file, line, column);
        """;

    /// <summary>Atomically replaces an index database with the supplied immutable snapshot.</summary>
    public static void Write(
        string databasePath,
        IReadOnlyList<DatabaseObject> objects,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<ObjectParameter> parameters,
        IReadOnlyList<ResultColumn> resultColumns,
        IReadOnlyList<TableKey> tableKeys,
        IReadOnlyList<TableForeignKey> foreignKeys,
        IReadOnlyList<TableIndexColumn> indexColumns,
        IReadOnlyList<ColumnUsage> columnUsage,
        IReadOnlyList<TableColumn> tableColumns,
        IReadOnlyList<ParseProblem> problems,
        IReadOnlyList<IndexWarning> warnings,
        IReadOnlyList<LintFinding> lintFindings,
        IReadOnlyList<IndexedFile> files,
        ReachResult reachability,
        string settings,
        string defaultSchema,
        string sourceRoot,
        IReadOnlyList<CachedBatch>? batches = null
    )
    {
        var snapshot = new IndexWriteSnapshot(
            objects,
            dependencies,
            parameters,
            resultColumns,
            tableKeys,
            foreignKeys,
            indexColumns,
            columnUsage,
            tableColumns,
            problems,
            warnings,
            lintFindings,
            files,
            reachability,
            settings,
            defaultSchema,
            sourceRoot,
            batches);

        WriteAtomically(databasePath, snapshot);
    }

    private static void WriteAtomically(string databasePath, IndexWriteSnapshot snapshot)
    {
        var temporaryPath = CreateTemporaryDatabasePath(databasePath);

        try
        {
            WriteNew(temporaryPath, snapshot);
            File.Move(temporaryPath, databasePath, overwrite: true);
        }
        finally
        {
            DeleteTemporaryDatabase(temporaryPath);
        }
    }

    private static string CreateTemporaryDatabasePath(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);

        return Path.Combine(
            directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    private static void DeleteTemporaryDatabase(string temporaryPath)
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static void WriteNew(string databasePath, IndexWriteSnapshot snapshot)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        using SqliteConnection connection = new(connectionString.ToString());
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using SqliteBatchInserter inserts = new(connection, transaction);

        CreateSchema(connection);
        InsertMetadata(inserts, snapshot.Settings, snapshot.DefaultSchema, snapshot.SourceRoot);
        InsertFiles(inserts, snapshot.Files);
        InsertBatches(inserts, snapshot.Batches);

        InsertObjects(inserts, snapshot.Objects);

        InsertDependencies(inserts, snapshot.Dependencies);

        InsertParameters(inserts, snapshot.Parameters);
        InsertResultColumns(inserts, snapshot.ResultColumns);
        InsertTableKeys(inserts, snapshot.TableKeys);
        InsertForeignKeys(inserts, snapshot.ForeignKeys);
        InsertIndexColumns(inserts, snapshot.IndexColumns);

        InsertColumnUsage(inserts, snapshot.ColumnUsage);
        InsertTableColumns(inserts, snapshot.TableColumns);

        InsertDiagnostics(inserts, snapshot.Problems, snapshot.Warnings, snapshot.LintFindings);

        InsertReachability(inserts, snapshot.Reachability);

        transaction.Commit();
    }

}
