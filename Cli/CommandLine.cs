using Microsoft.Data.Sqlite;

namespace SqlDeps;

/// <summary>Parses sqldeps commands and coordinates indexing, querying and presentation.</summary>
public static class CommandLine
{
    private const int MaxReachDepth = 12;

    private const string Usage = """
        usage:
          sqldeps index <folder> [--db <file>] [--default-schema <name>] [--full] [--quiet]
          sqldeps status <folder> [--db <file>] [--json] [--no-color]
          sqldeps who-writes|who-reads|deps|impact <object> [--db <file>] [--json] [--limit <n>] [--no-color]
          sqldeps duplicates|errors [--db <file>] [--json] [--limit <n>] [--no-color]
        """;

    private static readonly IReadOnlyDictionary<string, string> ObjectQueries = new Dictionary<string, string>
    {
        ["who-writes"] = "SELECT DISTINCT source_object, operation, source_file, line FROM writers WHERE target_object = $name ORDER BY source_object, operation, source_file, line",
        ["who-reads"] = "SELECT DISTINCT source_object, source_file, line FROM readers WHERE target_object = $name ORDER BY source_object, source_file, line",
        ["deps"] = "SELECT DISTINCT target_object, operation, source_file, line FROM dependencies WHERE source_object = $name ORDER BY target_object, operation, source_file, line",
        ["impact"] = "SELECT affected, depth, path FROM impact WHERE origin = $name ORDER BY depth, affected",
    };

    private static readonly IReadOnlyDictionary<string, string> ListQueries = new Dictionary<string, string>
    {
        ["duplicates"] = "SELECT name, kind, definitions, places FROM duplicate_definitions ORDER BY name, kind",
        ["errors"] = "SELECT source_file, line, message FROM parse_errors ORDER BY source_file, line",
    };

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
        var options = Parse(args, requireValue: true, allowSchema: true, allowFull: true);
        var folder = ExistingFolder(options.Value!);
        var databasePath = options.DatabasePath ?? DefaultDatabasePath(folder);
        var defaultSchema = options.DefaultSchema ?? ObjectNames.StandardDefaultSchema;
        var settings = IndexCache.SettingsFor(defaultSchema);
        var cache = options.FullReindex ? new Dictionary<string, CachedFile>() : IndexCache.Load(databasePath, settings);
        var indexer = new SqlFolderIndexer(defaultSchema, cache);
        indexer.Index(folder);
        var reachability = ReachabilityBuilder.Build(indexer.Dependencies, MaxReachDepth);

        SqliteStore.Write(databasePath, indexer.Objects, indexer.Dependencies, indexer.Problems, indexer.Files, reachability, settings);
        if (!options.Quiet)
        {
            Console.WriteLine("Indexed {0} .sql files ({1} reused unchanged): {2} objects, {3} dependencies, {4} parse errors.", indexer.FileCount, indexer.ReusedFileCount, indexer.Objects.Count, indexer.Dependencies.Count, indexer.Problems.Count);
            Console.WriteLine("Reach: {0} object pairs (up to {1} steps), {2} objects in a cycle.", reachability.Reach.Count, MaxReachDepth, reachability.Cycles.Count);
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
        ConsoleOutput.WriteRows(database.Query(WithLimit(ObjectQueries[command], options.Limit), ("$name", options.Value!), ("$limit", options.Limit)), options.Json, !options.NoColor);

        return 0;
    }

    private static int ListQuery(string command, string[] args)
    {
        var options = Parse(args, requireValue: false);
        ConsoleOutput.WriteRows(OpenDatabase(options.DatabasePath).Query(WithLimit(ListQueries[command], options.Limit), ("$limit", options.Limit)), options.Json, !options.NoColor);

        return 0;
    }

    private static IndexDatabase OpenDatabase(string? databasePath) => new(databasePath ?? DefaultDatabasePath(Directory.GetCurrentDirectory()));

    private static string ExistingFolder(string value)
    {
        var folder = Path.GetFullPath(value);

        if (!Directory.Exists(folder))
        {
            throw new ArgumentException("No such folder: " + folder);
        }

        return folder;
    }

    private static CommandOptions Parse(string[] args, bool requireValue, bool allowSchema = false, bool allowFull = false)
    {
        string? value = null;
        string? databasePath = null;
        string? defaultSchema = null;
        var full = false;
        var json = false;
        var quiet = false;
        var noColor = false;
        int? limit = null;

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

            else if (argument == "--limit" && index + 1 < args.Length && int.TryParse(args[++index], out var parsedLimit) && parsedLimit > 0)
            {
                limit = parsedLimit;
            }

            else if (!argument.StartsWith('-') && value is null)
            {
                value = argument;
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

        return new CommandOptions(value, databasePath, defaultSchema, full, json, quiet, noColor, limit);
    }

    private static string WithLimit(string sql, int? limit) => limit is null ? sql : sql + " LIMIT $limit";

    private static string DefaultDatabasePath(string folder)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "sqldeps", new DirectoryInfo(folder).Name + ".sqlite");
    }

    private sealed record CommandOptions(string? Value, string? DatabasePath, string? DefaultSchema, bool FullReindex, bool Json, bool Quiet, bool NoColor, int? Limit);
}
