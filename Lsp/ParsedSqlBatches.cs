using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

/// <summary>A successfully parsed SQL batch retained for LSP-only validators.</summary>
internal sealed record ParsedSqlBatch(TSqlScript Script, string Text, int StartLine);
