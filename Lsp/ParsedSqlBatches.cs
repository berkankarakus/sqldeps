using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Parses the valid SQL batches in an open document for LSP-only validators.</summary>
internal static class ParsedSqlBatches
{
    public static IEnumerable<ParsedSqlBatch> From(string text)
    {
        foreach (var batch in BatchSplitter.Split(text))
        {
            TSql160Parser parser = new(initialQuotedIdentifiers: true);
            using StringReader reader = new(batch.Text);
            var fragment = parser.Parse(reader, out IList<ParseError> errors);
            if (errors.Count == 0 && fragment is TSqlScript script)
            {
                yield return new(script, batch.Text, batch.StartLine);
            }
        }
    }
}

internal sealed record ParsedSqlBatch(TSqlScript Script, string Text, int StartLine);
