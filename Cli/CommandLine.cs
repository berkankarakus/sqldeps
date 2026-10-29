using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SqlDeps.Analysis;
using SqlDeps.Cli;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Parses sqldeps commands and coordinates indexing, querying and presentation.</summary>
public static class CommandLine
{
    private const int MaxReachDepth = 12;

    private const string Usage = """
        usage:
          sqldeps index <folder> [--db <file>] [--default-schema <name>] [--jobs <n>] [--full] [--quiet]
          sqldeps status <folder> [--db <file>] [--json] [--no-color]
          sqldeps use <index-name|database-file>
          sqldeps reindex [folder] [--db <file>]
          sqldeps indexes
          sqldeps unuse
          sqldeps drop <index-name|database-file> [--yes]
          sqldeps doctor
          sqldeps prompt
          sqldeps source <object> [--db <file>] [--json] [--no-color] [--no-pager]
          sqldeps find <text> [--db <file>] [--json] [--limit <n>] [--no-color] [--no-pager]
          sqldeps table-columns <table> [--db <file>] [--json] [--limit <n>] [--no-color] [--no-pager]
          sqldeps lint [--db <file>] [--json] [--limit <n>] [--no-color] [--no-pager]
          sqldeps who-writes|who-reads|deps|impact|columns <object> [--db <file>] [--json] [--table] [--limit <n>] [--no-color] [--no-pager]
          sqldeps who-updates <table> <column> [--db <file>] [--json] [--table] [--limit <n>] [--no-color] [--no-pager]
          sqldeps objects|duplicates|errors|warnings [--db <file>] [--json] [--limit <n>] [--no-color] [--no-pager]
          sqldeps lsp                                  # stdio LSP server (editor starts this)
        """;

    private static readonly IReadOnlyDictionary<string, string> ObjectQueries = new Dictionary<string, string>
    {
        ["who-writes"] = "SELECT DISTINCT source_object, operation, source_file, line FROM writers WHERE target_object = $name COLLATE NOCASE ORDER BY source_object, operation, source_file, line",
        ["who-reads"] = "SELECT DISTINCT source_object, source_file, line FROM readers WHERE target_object = $name COLLATE NOCASE ORDER BY source_object, source_file, line",
        ["deps"] = "SELECT DISTINCT target_object, operation, source_file, line FROM dependencies WHERE source_object = $name COLLATE NOCASE ORDER BY target_object, operation, source_file, line",
        ["impact"] = "SELECT affected, depth, path FROM impact WHERE origin = $name COLLATE NOCASE ORDER BY depth, affected",
        ["columns"] = "SELECT DISTINCT table_name, column_name, operation, source_file, line FROM column_usage WHERE source_object = $name COLLATE NOCASE ORDER BY source_file, line, operation, table_name, column_name",
    };

    private static readonly IReadOnlyDictionary<string, string> ListQueries = new Dictionary<string, string>
    {
        ["objects"] = "SELECT DISTINCT name, kind, source_file, line FROM objects ORDER BY name, kind, source_file, line",
        ["duplicates"] = "SELECT name, kind, definitions, places FROM duplicate_definitions ORDER BY name, kind",
        ["errors"] = "SELECT source_file, line, message FROM parse_errors ORDER BY source_file, line",
        ["warnings"] = "SELECT source_file, line, message FROM parse_warnings ORDER BY source_file, line",
    };

    private const string LintQuery = """
        SELECT severity, rule, object_name, source_file, line, message
        FROM (
            SELECT severity, rule, source_object AS object_name, source_file, line, message
            FROM lint_findings

            UNION ALL

            SELECT 'error' AS severity, 'parse-error' AS rule,
                   source_file AS object_name, source_file, line, message
            FROM parse_errors

            UNION ALL

            SELECT 'warning', 'dynamic-sql', source_object, source_file, line,
                   'Uses unresolved dynamic SQL; dependency analysis may be incomplete.'
            FROM dependencies
            WHERE operation = 'EXEC_DYNAMIC'

            UNION ALL

            SELECT 'warning', 'duplicate-definition', name, '', 0,
                   'Has ' || definitions || ' definitions: ' || places
            FROM duplicate_definitions

            UNION ALL

            SELECT 'info', 'unused-object', name, source_file, line,
                   'Defined in the index but not referenced by another indexed object.'
            FROM unused_objects

            UNION ALL

            SELECT DISTINCT 'info', 'cross-database-dependency', source_object, source_file, line,
                   'References ' || target_object || ' outside the current database.'
            FROM dependencies
            WHERE length(target_object) - length(replace(target_object, '.', '')) >= 2
        )
        ORDER BY CASE severity WHEN 'error' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END,
                 rule, object_name, source_file, line
        """;

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException(Usage);
            }

            return args[0] switch
            {
                "index" => Index(args[1..]),
                "status" => Status(args[1..]),
                "use" => Use(args[1..]),
                "reindex" => Reindex(args[1..]),
                "indexes" => Indexes(args[1..]),
                "unuse" => Unuse(args[1..]),
                "drop" => Drop(args[1..]),
                "doctor" => Doctor(args[1..]),
                "prompt" => Prompt(args[1..]),
                "source" => Source(args[1..]),
                "find" => Find(args[1..]),
                "table-columns" => TableColumns(args[1..]),
                "lint" => Lint(args[1..]),
                "who-updates" => WhoUpdates(args[1..]),
                var command when ObjectQueries.ContainsKey(command) => ObjectQuery(command, args[1..]),
                var command when ListQueries.ContainsKey(command) => ListQuery(command, args[1..]),
                _ => throw new ArgumentException("Unknown command: " + args[0] + "\n" + Usage),
            };
        }

        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SqliteException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Index(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = Parse(args, requireValue: true, allowSchema: true, allowFull: true, allowJobs: true);
        var folder = ExistingFolder(options.Value!);
        var databasePath = options.DatabasePath ?? DefaultDatabasePath(folder);
        var defaultSchema = options.DefaultSchema ?? ObjectNames.StandardDefaultSchema;
        var settings = IndexCache.SettingsFor(defaultSchema);
        var cache = options.FullReindex
            ? []
            : IndexCache.Load(databasePath, settings);
        SqlFolderIndexer indexer = new(defaultSchema, cache, options.Jobs);
        indexer.Index(folder);
        var indexedAt = stopwatch.Elapsed;
        var reachability = ReachabilityBuilder.Build(indexer.Dependencies, MaxReachDepth);
        var reachedAt = stopwatch.Elapsed;

        SqliteStore.Write(databasePath, indexer.Objects, indexer.Dependencies, indexer.Parameters, indexer.ColumnUsage, indexer.TableColumns, indexer.Problems, indexer.Warnings, indexer.LintFindings, indexer.Files, reachability, settings, defaultSchema, folder);
        stopwatch.Stop();

        if (!options.Quiet)
        {
            Console.WriteLine("Indexed {0} .sql files ({1} reused unchanged): {2} objects, {3} dependencies, {4} parse errors, {5} warnings.", indexer.FileCount, indexer.ReusedFileCount, indexer.Objects.Count, indexer.Dependencies.Count, indexer.Problems.Count, indexer.Warnings.Count);
            Console.WriteLine("Reach: {0} object pairs (up to {1} steps), {2} objects in a cycle.", reachability.Reach.Count, MaxReachDepth, reachability.Cycles.Count);
            Console.WriteLine("Timing: parse {0:F2}s, reach {1:F2}s, SQLite {2:F2}s", indexedAt.TotalSeconds, (reachedAt - indexedAt).TotalSeconds, (stopwatch.Elapsed - reachedAt).TotalSeconds);
            Console.WriteLine("Elapsed: {0:F2}s", stopwatch.Elapsed.TotalSeconds);
            Console.WriteLine("Database: " + databasePath);
        }

        return 0;
    }

    private static int Status(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var folder = ExistingFolder(options.Value!);
        var status = new IndexDatabase(options.DatabasePath ?? DefaultDatabasePath(folder)).GetStatus(folder);
        ConsoleOutput.WriteStatus(status, options.Json, !options.NoColor);

        return status.IsCurrent ? 0 : 1;
    }

    private static int ObjectQuery(string command, string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var objectName = database.QualifyObjectName(options.Value!);
        var rows = database.Query(WithLimit(ObjectQueries[command], options.Limit), ("$name", objectName), ("$limit", options.Limit));

        if (options.Table)
        {
            ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        }
        else
        {
            ConsoleOutput.WriteObjectRows(command, objectName, rows, options.Json, !options.NoColor, !options.NoPager);
        }

        return 0;
    }

    private static int WhoUpdates(string[] args)
    {
        var options = Parse(args, requireValue: true, maximumValues: 2);

        if (options.SecondValue is null)
        {
            throw new ArgumentException(Usage);
        }

        const string query = """
            SELECT DISTINCT source_object, operation, source_file, line
            FROM column_usage
            WHERE table_name = $table COLLATE NOCASE
              AND column_name = $column COLLATE NOCASE
              AND operation = 'UPDATE'
            ORDER BY source_object, source_file, line
            """;
        var database = OpenDatabase(options.DatabasePath);
        var tableName = database.QualifyObjectName(options.Value!);
        var rows = database.Query(
            WithLimit(query, options.Limit),
            ("$table", tableName),
            ("$column", options.SecondValue),
            ("$limit", options.Limit)
        );
        var target = tableName + "." + options.SecondValue;

        if (options.Table)
        {
            ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        }
        else
        {
            ConsoleOutput.WriteObjectRows("who-updates", target, rows, options.Json, !options.NoColor, !options.NoPager);
        }

        return 0;
    }

    private static int Use(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException(Usage);
        }

        var databasePath = args[0].EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
            || args[0].Contains(Path.DirectorySeparatorChar)
            ? Path.GetFullPath(args[0])
            : DefaultDatabasePath(args[0]);

        if (!File.Exists(databasePath))
        {
            throw new ArgumentException("No index database: " + databasePath);
        }

        var activePath = ActiveDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        File.WriteAllText(activePath, databasePath + Environment.NewLine);
        Console.WriteLine("Using index: " + databasePath);

        return 0;
    }

    private static int Prompt(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException(Usage);
        }

        var activePath = ActiveDatabasePath();

        if (!File.Exists(activePath))
        {
            return 0;
        }

        var databasePath = File.ReadAllText(activePath).Trim();

        if (databasePath != "" && File.Exists(databasePath))
        {
            Console.WriteLine(Path.GetFileNameWithoutExtension(databasePath));
        }

        return 0;
    }

    private static int Source(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var objectName = database.QualifyObjectName(options.Value!);
        var rows = database.Query(
            "SELECT DISTINCT kind, source_file, line FROM objects WHERE name = $name COLLATE NOCASE ORDER BY source_file, line",
            ("$name", objectName)
        );
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static int Find(string[] args)
    {
        var options = Parse(args, requireValue: true);
        const string query = """
            SELECT DISTINCT name, kind, source_file, line
            FROM objects
            WHERE name LIKE $pattern ESCAPE '!' COLLATE NOCASE
            ORDER BY name, kind, source_file, line
            """;
        var rows = OpenDatabase(options.DatabasePath).Query(
            WithLimit(query, options.Limit),
            ("$pattern", "%" + EscapeLike(options.Value!) + "%"),
            ("$limit", options.Limit)
        );
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static int TableColumns(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var tableName = database.QualifyObjectName(options.Value!);
        const string query = "SELECT column_name, data_type, source_file, line FROM table_columns WHERE table_name = $table COLLATE NOCASE ORDER BY column_name, source_file, line";
        var rows = database.Query(WithLimit(query, options.Limit), ("$table", tableName), ("$limit", options.Limit));
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static int Reindex(string[] args)
    {
        if (args.Length == 0)
        {
            var database = OpenDatabase(null);
            var sourceRoot = database.SourceRoot ?? throw new InvalidOperationException("This index has no recorded source folder; run sqldeps index <folder> --db " + database.Path + " first.");
            return Index([sourceRoot, "--db", database.Path, "--full"]);
        }

        return Index([.. args, "--full"]);
    }

    private static int Indexes(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException(Usage);
        }

        var directory = IndexDirectory();
        var active = ActiveDatabasePathOrDefault();
        List<Dictionary<string, object?>> rows = [];

        if (Directory.Exists(directory))
        {
            foreach (var databasePath in Directory.EnumerateFiles(directory, "*.sqlite"))
            {
                var database = new IndexDatabase(databasePath);
                rows.Add(new Dictionary<string, object?>
                {
                    ["name"] = Path.GetFileNameWithoutExtension(databasePath),
                    ["active"] = string.Equals(databasePath, active, StringComparison.Ordinal) ? "*" : "",
                    ["source_root"] = database.SourceRoot ?? "(legacy index)",
                });
            }
        }

        ConsoleOutput.WriteRows(rows, asJson: false, useColor: true, usePager: true);
        return 0;
    }

    private static int Unuse(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException(Usage);
        }

        var activePath = ActiveDatabasePath();
        if (File.Exists(activePath)) File.Delete(activePath);
        return 0;
    }

    private static int Drop(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--yes"))
        {
            throw new ArgumentException(Usage);
        }

        var databasePath = ResolveDatabasePath(args[0]);
        if (!File.Exists(databasePath)) throw new ArgumentException("No index database: " + databasePath);

        var indexName = Path.GetFileNameWithoutExtension(databasePath);
        if (args.Length == 1 && !ConfirmDrop(indexName))
        {
            Console.WriteLine("Deletion cancelled.");
            return 0;
        }

        File.Delete(databasePath);

        var activePath = ActiveDatabasePath();
        if (File.Exists(activePath) && string.Equals(File.ReadAllText(activePath).Trim(), databasePath, StringComparison.Ordinal)) File.Delete(activePath);
        Console.WriteLine("Deleted index '{0}': {1}", indexName, databasePath);
        return 0;
    }

    private static bool ConfirmDrop(string indexName)
    {
        Console.Write("Delete index '{0}'? [y/N] ", indexName);
        var answer = Console.ReadLine();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int Doctor(string[] args)
    {
        if (args.Length != 0) throw new ArgumentException(Usage);
        var database = OpenDatabase(null);
        var sourceRoot = database.SourceRoot ?? throw new InvalidOperationException("This index has no recorded source folder.");
        if (!Directory.Exists(sourceRoot)) throw new InvalidOperationException("Indexed source folder no longer exists: " + sourceRoot);
        var status = database.GetStatus(sourceRoot);
        ConsoleOutput.WriteStatus(status, asJson: false, useColor: true);
        return status.IsCurrent ? 0 : 1;
    }

    private static int ListQuery(string command, string[] args)
    {
        var options = Parse(args, requireValue: false);
        ConsoleOutput.WriteRows(OpenDatabase(options.DatabasePath).Query(WithLimit(ListQueries[command], options.Limit), ("$limit", options.Limit)), options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static int Lint(string[] args)
    {
        var options = Parse(args, requireValue: false);
        var rows = OpenDatabase(options.DatabasePath).Query(WithLimit(LintQuery, options.Limit), ("$limit", options.Limit));
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static IndexDatabase OpenDatabase(string? databasePath) => new(databasePath ?? ActiveDatabasePathOrDefault());

    private static string ExistingFolder(string value)
    {
        var folder = Path.GetFullPath(value);

        if (!Directory.Exists(folder))
        {
            throw new ArgumentException("No such folder: " + folder);
        }

        return folder;
    }

    private static CommandOptions Parse(string[] args, bool requireValue, bool allowSchema = false, bool allowFull = false, bool allowJobs = false, int maximumValues = 1)
    {
        string? value = null;
        string? secondValue = null;
        string? databasePath = null;
        string? defaultSchema = null;
        var full = false;
        var json = false;
        var quiet = false;
        var noColor = false;
        var noPager = false;
        var table = false;
        int? limit = null;
        int? jobs = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--db" && index + 1 < args.Length)
            {
                databasePath = Path.GetFullPath(args[++index]);
            }

            else if (argument == "--default-schema" && allowSchema && index + 1 < args.Length)
            {
                defaultSchema = args[++index];
            }

            else if (argument == "--full" && allowFull)
            {
                full = true;
            }

            else if (argument == "--json")
            {
                json = true;
            }

            else if (argument == "--quiet")
            {
                quiet = true;
            }

            else if (argument == "--no-color")
            {
                noColor = true;
            }

            else if (argument == "--no-pager")
            {
                noPager = true;
            }

            else if (argument == "--table")
            {
                table = true;
            }

            else if (argument == "--limit" && index + 1 < args.Length && int.TryParse(args[++index], out var parsedLimit) && parsedLimit > 0)
            {
                limit = parsedLimit;
            }

            else if (argument == "--jobs" && allowJobs && index + 1 < args.Length && int.TryParse(args[++index], out var parsedJobs) && parsedJobs > 0)
            {
                jobs = parsedJobs;
            }

            else if (!argument.StartsWith('-') && value is null)
            {
                value = argument;
            }

            else if (!argument.StartsWith('-') && maximumValues > 1 && secondValue is null)
            {
                secondValue = argument;
            }

            else
            {
                throw new ArgumentException("Unknown or incomplete option: " + argument + "\n" + Usage);
            }
        }

        if (requireValue && value is null)
        {
            throw new ArgumentException(Usage);
        }

        return new CommandOptions(value, secondValue, databasePath, defaultSchema, full, json, quiet, noColor, noPager, table, limit, jobs);
    }

    private static string WithLimit(string sql, int? limit) => limit is null ? sql : sql + " LIMIT $limit";

    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    private static string DefaultDatabasePath(string folder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", new DirectoryInfo(folder).Name + ".sqlite");
    }

    private static string ResolveDatabasePath(string value) => value.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || value.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(value) : DefaultDatabasePath(value);

    private static string IndexDirectory() => Path.GetDirectoryName(ActiveDatabasePath())!;

    private static string ActiveDatabasePathOrDefault()
    {
        var activePath = ActiveDatabasePath();

        if (File.Exists(activePath))
        {
            var databasePath = File.ReadAllText(activePath).Trim();

            if (databasePath != "" && File.Exists(databasePath))
            {
                return databasePath;
            }
        }

        return DefaultDatabasePath(Directory.GetCurrentDirectory());
    }

    private static string ActiveDatabasePath()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("SQLDEPS_HOME");

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.Combine(Path.GetFullPath(configuredDirectory), "active-index");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", "active-index");
    }

    private sealed record CommandOptions(string? Value, string? SecondValue, string? DatabasePath, string? DefaultSchema, bool FullReindex, bool Json, bool Quiet, bool NoColor, bool NoPager, bool Table, int? Limit, int? Jobs);
}
