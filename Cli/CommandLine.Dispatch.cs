using System.Reflection;
using Microsoft.Data.Sqlite;
using SqlDeps.Storage;

namespace SqlDeps;

/// <summary>Owns command discovery, usage, version, and top-level error handling.</summary>
public static partial class CommandLine
{
    private const string Usage = """
        usage:
          # index management
          sqldeps index <folder> [--db <file>] [--default-schema <name>] [--jobs <n>] [--full] [--quiet] [--json]
          sqldeps status <folder> [--db <file>] [--json] [--no-color]
          sqldeps reindex [folder] [--db <file>] [--json]
          sqldeps use <index-name|database-file>
          sqldeps drop <index-name|database-file> [--yes] [--json]

          # inspection and analysis
          sqldeps objects [--db <file>] [--json] [--limit <n>]
          sqldeps find <text> [--db <file>] [--json] [--limit <n>]
          sqldeps source <object> [--db <file>] [--json]
          sqldeps graph [--db <file>] [--format json|dot] [--limit <n>]
          sqldeps deps|impact|columns|who-reads|who-writes <object> [--db <file>] [--json] [--limit <n>]
          sqldeps table-columns <table> [--db <file>] [--json] [--limit <n>]
          sqldeps lint [--db <file>] [--json|--sarif] [--profile <name>] [--limit <n>] [--fail-on <severity>]
          sqldeps query-builder <table-or-view> [--join] [--db <file>] [--json]

          # maintenance and automation
          sqldeps indexes|unuse|doctor|drift|report|prompt [options]
          sqldeps duplicates|errors|warnings [--db <file>] [--json] [--limit <n>]
          sqldeps who-updates <table> <column> [--db <file>] [--json] [--limit <n>]
          sqldeps explain <rule> [--json]
          sqldeps test-skeleton <procedure> [--db <file>] [--json]
          sqldeps refactor-candidates <file> [--db <file>] [--default-schema <name>] [--json]

          # integrations
          sqldeps lsp                                  # stdio LSP server (editor starts this)
          sqldeps mcp [--db <file>]                    # stdio MCP server

          # general
          sqldeps --help                              # show this list
          sqldeps --version                            # show the installed version
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
            Console.WriteLine(ReadVersionText());
            Console.WriteLine(ReadScriptDomIdentity());

            return 0;
        }

        return DispatchCommand(args);
    }

    /// <summary>Identifies the loaded ScriptDom build by version and file hash so a local fork can be told apart.</summary>
    internal static string ReadScriptDomIdentity()
    {
        var assembly = typeof(Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var path = assembly.Location;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return $"ScriptDom: {version}";
        }

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));

        return $"ScriptDom: {version} sha256:{hash[..12].ToLowerInvariant()}";
    }

    /// <summary>Returns the informational version (including the build commit when stamped), else the assembly version.</summary>
    internal static string ReadVersionText()
    {
        var assembly = typeof(CommandLine).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = assembly.GetName().Version;

        if (assemblyVersion is null)
        {
            return "unknown";
        }

        return assemblyVersion.ToString();
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
            "graph" => Graph(args[1..]),
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
