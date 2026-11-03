using SqlDeps;
using SqlDeps.Lsp;
using SqlDeps.Mcp;

return args is ["lsp"]
    ? LanguageServer.Run()
    : args is ["mcp", .. var mcpArgs]
        ? McpServer.Run(mcpArgs)
        : CommandLine.Run(args);
