using SqlDeps.Cli;
using SqlDeps.Storage;

namespace SqlDeps;

internal static partial class CommandLine
{
    private static int Use(string[] args)
    {
        var options = Parse(args, requireValue: true);
        EnsureIndexSelectionOptions(options, allowValue: true);
        var databasePath = ResolveDatabasePath(options.Value!);

        if (!File.Exists(databasePath))
        {
            throw new ArgumentException("No index database: " + databasePath);
        }

        var activePath = ActiveDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        File.WriteAllText(activePath, databasePath + Environment.NewLine);
        if (options.Json)
        {
            ConsoleOutput.WriteActiveIndex(databasePath, active: true);
        }
        else
        {
            Console.WriteLine("Using index: " + databasePath);
        }

        return 0;
    }

    private static int Prompt(string[] args)
    {
        var options = Parse(args, requireValue: false);
        EnsureIndexSelectionOptions(options, allowValue: false);
        var activePath = ActiveDatabasePath();
        if (!File.Exists(activePath))
        {
            return 0;
        }

        var databasePath = File.ReadAllText(activePath).Trim();
        if (databasePath != "" && File.Exists(databasePath))
        {
            if (options.Json)
            {
                ConsoleOutput.WriteActiveIndex(databasePath, active: true);
            }
            else
            {
                Console.WriteLine(Path.GetFileNameWithoutExtension(databasePath));
            }
        }
        else if (options.Json)
        {
            ConsoleOutput.WriteActiveIndex(null, active: false);
        }

        return 0;
    }

    private static int Reindex(string[] args)
    {
        if (args.Length == 0)
        {
            var database = OpenDatabase(null);
            var sourceRoot = database.SourceRoot ?? throw new InvalidOperationException("This index has no recorded source folder; run sqldeps index <folder> --db " + database.Path + " first.");
            List<string> reindexArguments = [
                sourceRoot,
                "--db",
                database.Path,
                "--default-schema",
                database.DefaultSchema,
                "--full",
            ];

            if (database.DefaultDatabase is not null)
            {
                reindexArguments.Add("--default-database");
                reindexArguments.Add(database.DefaultDatabase);
            }

            foreach (var pattern in database.IncludePatterns)
            {
                reindexArguments.Add("--include");
                reindexArguments.Add(pattern);
            }

            foreach (var pattern in database.ExcludePatterns)
            {
                reindexArguments.Add("--exclude");
                reindexArguments.Add(pattern);
            }

            return Index([.. reindexArguments]);
        }
        return Index([.. args, "--full"]);
    }

    private static int Indexes(string[] args)
    {
        var options = Parse(args, requireValue: false);
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
                    ["active"] = options.Json ? string.Equals(databasePath, active, StringComparison.Ordinal) : string.Equals(databasePath, active, StringComparison.Ordinal) ? "*" : "",
                    ["source_root"] = database.SourceRoot ?? "(legacy index)",
                });
            }
        }
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int Unuse(string[] args)
    {
        var options = Parse(args, requireValue: false);
        EnsureIndexSelectionOptions(options, allowValue: false);
        var activePath = ActiveDatabasePath();
        if (File.Exists(activePath))
        {
            File.Delete(activePath);
        }

        if (options.Json)
        {
            ConsoleOutput.WriteActiveIndex(null, active: false);
        }

        return 0;
    }

    private static void EnsureIndexSelectionOptions(CommandOptions options, bool allowValue)
    {
        if (!allowValue && options.Value is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.SecondValue is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.DatabasePath is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.DefaultSchema is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.DefaultDatabase is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.FullReindex)
        {
            throw new ArgumentException(Usage);
        }

        if (options.Jobs is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.FailOn is not null)
        {
            throw new ArgumentException(Usage);
        }

        if (options.Table)
        {
            throw new ArgumentException(Usage);
        }
    }

    private static int Drop(string[] args)
    {
        ParseDropArguments(args, out var value, out var yes, out var json);

        if (value is null)
        {
            throw new ArgumentException(Usage);
        }

        var databasePath = ResolveDatabasePath(value);

        if (!File.Exists(databasePath))
        {
            throw new ArgumentException("No index database: " + databasePath);
        }

        var indexName = Path.GetFileNameWithoutExtension(databasePath);

        if (!yes && !ConfirmDrop(indexName, json))
        {
            if (json)
            {
                ConsoleOutput.WriteDropResult(indexName, databasePath, deleted: false, cancelled: true);
            }
            else
            {
                Console.WriteLine("Deletion cancelled.");
            }

            return 0;
        }

        File.Delete(databasePath);
        RemoveActiveIndexIfMatches(databasePath);

        if (json)
        {
            ConsoleOutput.WriteDropResult(indexName, databasePath, deleted: true, cancelled: false);
        }
        else
        {
            Console.WriteLine("Deleted index '{0}': {1}", indexName, databasePath);
        }

        return 0;
    }

    private static void ParseDropArguments(string[] args, out string? value, out bool yes, out bool json)
    {
        value = null;
        yes = false;
        json = false;

        foreach (var argument in args)
        {
            if (argument == "--yes")
            {
                yes = true;

                continue;
            }

            if (argument == "--json")
            {
                json = true;

                continue;
            }

            if (value is null && !argument.StartsWith('-'))
            {
                value = argument;

                continue;
            }

            throw new ArgumentException(Usage);
        }
    }

    private static void RemoveActiveIndexIfMatches(string databasePath)
    {
        var activePath = ActiveDatabasePath();

        if (!File.Exists(activePath))
        {
            return;
        }

        if (string.Equals(File.ReadAllText(activePath).Trim(), databasePath, StringComparison.Ordinal))
        {
            File.Delete(activePath);
        }
    }

    private static bool ConfirmDrop(string indexName, bool json)
    {
        var writer = json ? Console.Error : Console.Out;
        writer.Write("Delete index '{0}'? [y/N] ", indexName);
        var answer = Console.ReadLine();

        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int Doctor(string[] args)
    {
        var options = Parse(args, requireValue: false);
        var database = OpenDatabase(null);
        var sourceRoot = database.SourceRoot ?? throw new InvalidOperationException("This index has no recorded source folder.");
        if (!Directory.Exists(sourceRoot))
        {
            throw new InvalidOperationException("Indexed source folder no longer exists: " + sourceRoot);
        }

        var status = database.GetStatus(sourceRoot);
        ConsoleOutput.WriteStatus(status, options.Json, !options.NoColor);
        return status.IsCurrent ? 0 : 1;
    }

    private static int Drift(string[] args)
    {
        var options = Parse(args, requireValue: false);
        var database = OpenDatabase(options.DatabasePath);
        var folder = ResolveSourceFolder(database, options.Value);

        var rows = database.GetDrift(folder)
            .Select(item => new Dictionary<string, object?>
            {
                ["state"] = item.State,
                ["path"] = item.Path,
            })
            .ToArray();
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);

        return rows.Length == 0 ? 0 : 1;
    }

    private static int Report(string[] args)
    {
        var options = Parse(args, requireValue: false);
        var database = OpenDatabase(options.DatabasePath);
        var folder = ResolveSourceFolder(database, options.Value);

        var status = database.GetStatus(folder);
        var drift = database.GetDrift(folder);
        var lintRows = new IndexQueryService(database).Lint();
        var lintCounts = lintRows
            .GroupBy(row => row["severity"]?.ToString() ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key.ToLowerInvariant(), group => group.Count(), StringComparer.OrdinalIgnoreCase);

        ConsoleOutput.WriteReport(status, drift, lintCounts, options.Json, !options.NoColor, !options.NoPager);

        return status.IsCurrent ? 0 : 1;
    }

    private static string ResolveSourceFolder(IndexDatabase database, string? requestedFolder)
    {
        var folder = requestedFolder ?? database.SourceRoot;

        if (folder is null)
        {
            throw new InvalidOperationException("This index has no recorded source folder; pass a folder explicitly.");
        }

        if (!Directory.Exists(folder))
        {
            throw new InvalidOperationException("Indexed source folder no longer exists: " + folder);
        }

        return folder;
    }
}
