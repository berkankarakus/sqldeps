using SqlDeps;
using SqlDeps.Lsp;

return args is ["lsp"] ? LanguageServer.Run() : CommandLine.Run(args);
