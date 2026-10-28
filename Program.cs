using SqlDeps;

const string Usage = "usage: sqldeps index <folder> [--db <file>] [--default-schema <name>] [--full]";

if (args.Length < 2 || args[0] != "index")
{
    Console.Error.WriteLine(Usage);

    return 2;
}

var folder = Path.GetFullPath(args[1]);

if (!Directory.Exists(folder))
{
    Console.Error.WriteLine("No such folder: " + folder);

    return 2;
}

var databasePath = DefaultDatabasePath(folder);
var defaultSchema = ObjectNames.StandardDefaultSchema;
var fullReindex = false;

for (var index = 2; index < args.Length; index++)
{
    if (args[index] == "--db" && index + 1 < args.Length)
    {
        databasePath = Path.GetFullPath(args[index + 1]);
        index++;

        continue;
    }

    if (args[index] == "--default-schema" && index + 1 < args.Length)
    {
        defaultSchema = args[index + 1];
        index++;

        continue;
    }

    if (args[index] == "--full")
    {
        fullReindex = true;

        continue;
    }

    Console.Error.WriteLine("Unknown option: " + args[index]);
    Console.Error.WriteLine(Usage);

    return 2;
}

var settings = IndexCache.SettingsFor(defaultSchema);
var cache = fullReindex
    ? new Dictionary<string, CachedFile>()
    : IndexCache.Load(databasePath, settings);

var indexer = new SqlFolderIndexer(defaultSchema, cache);
indexer.Index(folder);

const int MaxReachDepth = 12;
var reachability = ReachabilityBuilder.Build(indexer.Dependencies, MaxReachDepth);

SqliteStore.Write(
    databasePath,
    indexer.Objects,
    indexer.Dependencies,
    indexer.Problems,
    indexer.Files,
    reachability,
    settings
);

Console.WriteLine(
    "Indexed {0} .sql files ({1} reused unchanged): {2} objects, {3} dependencies, {4} parse errors.",
    indexer.FileCount,
    indexer.ReusedFileCount,
    indexer.Objects.Count,
    indexer.Dependencies.Count,
    indexer.Problems.Count
);
Console.WriteLine(
    "Reach: {0} object pairs (up to {1} steps), {2} objects in a cycle.",
    reachability.Reach.Count,
    MaxReachDepth,
    reachability.Cycles.Count
);
Console.WriteLine("Database: " + databasePath);

foreach (var problem in indexer.Problems.Take(5))
{
    Console.WriteLine("  parse error {0}:{1}: {2}", problem.SourceFile, problem.Line, problem.Message);
}

if (indexer.Problems.Count > 5)
{
    Console.WriteLine("  ... {0} more in the parse_errors table", indexer.Problems.Count - 5);
}

Console.WriteLine();
Console.WriteLine("Try:");
Console.WriteLine("  sqlite3 \"{0}\" \"SELECT target_object, operation, source_file FROM dependencies WHERE source_object = 'dbo.MyProc';\"", databasePath);

return 0;

static string DefaultDatabasePath(string folder)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var name = new DirectoryInfo(folder).Name;

    return Path.Combine(home, ".local", "share", "sqldeps", name + ".sqlite");
}
