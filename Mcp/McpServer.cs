using System.Text.Json;
using SqlDeps.Storage;

namespace SqlDeps.Mcp;

/// <summary>Serves named, read-only index queries over the MCP stdio transport.</summary>
public static partial class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string UnknownVersion = "unknown";
    private const string DefaultSourceFile = "(mcp)";
    private const string JsonRpcVersion = "2.0";
    private const int JsonRpcParseErrorCode = -32700;
    private const int JsonRpcInvalidRequestCode = -32600;
    private const int JsonRpcMethodNotFoundCode = -32601;
    private const int JsonRpcInvalidParametersCode = -32602;
    private const int JsonRpcInternalErrorCode = -32603;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs the read-only MCP stdio server and returns a CLI-compatible exit code.</summary>
    public static int Run(string[] args)
    {
        try
        {
            var database = new IndexDatabase(ParseDatabasePath(args));
            var server = new McpServerSession(database);
            server.Run();
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static string ParseDatabasePath(string[] args)
    {
        string? databasePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] != "--db")
            {
                throw new ArgumentException("Unknown MCP option: " + args[index]);
            }

            if (++index == args.Length)
            {
                throw new ArgumentException("MCP option --db requires a file path.");
            }

            databasePath = Path.GetFullPath(args[index]);
        }

        return databasePath ?? IndexPathResolver.ActiveDatabasePathOrDefault();
    }

    private sealed partial class McpServerSession(IndexDatabase database)
    {
        private const int MaxRequestCharacters = 32 * 1024 * 1024;
        private const int MaxAnalysisCharacters = 16 * 1024 * 1024;
        private const int MaxParameterizationCharacters = 1024 * 1024;
        private const int MaxCodeActions = 256;
        private readonly IndexQueryService queries = new(database);

        private readonly record struct TextRequest(string SourceFile, string Text);

    }
}
