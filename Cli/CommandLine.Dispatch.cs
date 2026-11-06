using Microsoft.Data.Sqlite;
using SqlDeps.Core;

namespace SqlDeps;

/// <summary>Owns command discovery, usage, version, and top-level error handling.</summary>
public static partial class CommandLine
{
    private const string Usage = """
        usage:
          # temel indeks işlemleri
          sqldeps index <folder> [--db <file>] [--default-schema <name>] [--jobs <n>] [--full] [--quiet] [--json]
          sqldeps status <folder> [--db <file>] [--json] [--no-color]
          sqldeps reindex [folder] [--db <file>] [--json]
          sqldeps use <index-name|database-file>
          sqldeps drop <index-name|database-file> [--yes] [--json]

          # günlük inceleme ve analiz
          sqldeps objects [--db <file>] [--json] [--limit <n>]
          sqldeps find <text> [--db <file>] [--json] [--limit <n>]
          sqldeps source <object> [--db <file>] [--json]
          sqldeps deps|impact|columns|who-reads|who-writes <object> [--db <file>] [--json] [--limit <n>]
          sqldeps table-columns <table> [--db <file>] [--json] [--limit <n>]
          sqldeps lint [--db <file>] [--json] [--limit <n>] [--fail-on <severity>]
          sqldeps query-builder <table-or-view> [--join] [--db <file>] [--json]

          # ileri bakım ve otomasyon
          sqldeps indexes|unuse|doctor|drift|report|prompt [options]
          sqldeps duplicates|errors|warnings [--db <file>] [--json] [--limit <n>]
          sqldeps who-updates <table> <column> [--db <file>] [--json] [--limit <n>]
          sqldeps explain <rule> [--json]
          sqldeps test-skeleton <procedure> [--db <file>] [--json]
          sqldeps refactor-candidates <file> [--db <file>] [--default-schema <name>] [--json]

          # entegrasyon
          sqldeps lsp                                  # stdio LSP server (editor starts this)
          sqldeps mcp [--db <file>]                    # stdio MCP server

          # genel
          sqldeps --help                              # bu listeyi göster
          sqldeps --version                            # kurulu sürümü göster
        """;

    public static int Run(string[] args)
    {
        try
        {
            return RunCommand(args);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SqliteException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int RunCommand(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(Usage);
        }

        if (IsHelpRequest(args))
        {
            Console.WriteLine(Usage);

            return 0;
        }

        if (IsVersionRequest(args))
        {
            Console.WriteLine(typeof(CommandLine).Assembly.GetName().Version?.ToString() ?? "unknown");

            return 0;
        }

        return DispatchCommand(args);
    }

    private static bool IsHelpRequest(IReadOnlyList<string> args)
    {
        return args[0] is "--help" or "-h" or "help"
            || args.Skip(1).Any(argument => argument is "--help" or "-h");
    }

    private static bool IsVersionRequest(IReadOnlyList<string> args)
    {
        return args[0] is "--version" or "version";
    }

    private static int DispatchCommand(string[] args)
    {
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
            "drift" => Drift(args[1..]),
            "report" => Report(args[1..]),
            "prompt" => Prompt(args[1..]),
            "source" => Source(args[1..]),
            "find" => Find(args[1..]),
            "table-columns" => TableColumns(args[1..]),
            "query-builder" => QueryBuilder(args[1..]),
            "test-skeleton" => TestSkeleton(args[1..]),
            "refactor-candidates" => RefactorCandidates(args[1..]),
            "lint" => Lint(args[1..]),
            "explain" => Explain(args[1..]),
            "who-updates" => WhoUpdates(args[1..]),
            var command when IndexQueryService.IsObjectQuery(command) => ObjectQuery(command, args[1..]),
            var command when IndexQueryService.IsListQuery(command) => ListQuery(command, args[1..]),
            _ => throw new ArgumentException("Unknown command: " + args[0] + "\n" + Usage),
        };
    }
}
