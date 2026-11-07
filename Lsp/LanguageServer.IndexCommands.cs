using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private string ExecuteIndexSuggestionCommand(JsonElement parameters)
    {
        if (!TryGetIndexSuggestionArguments(parameters, out var tableName, out var columnName))
        {
            throw new ArgumentException("sqldeps.index-suggestion requires a table and column argument.");
        }

        var table = AllObjects()
            .Select(item => item.Object)
            .FirstOrDefault(item => SameName(item.Name, tableName));

        if (!IsTableOrView(table))
        {
            throw new ArgumentException($"No indexed table or view: {tableName}");
        }

        if (!HasIndexedColumn(table.Name, columnName))
        {
            throw new ArgumentException($"No indexed column metadata: {tableName}.{columnName}");
        }

        var indexName = $"IX_{table.Name.Replace('.', '_')}_{columnName}";

        return $"CREATE INDEX {QuoteIdentifier(indexName)} ON {QuoteObjectName(table.Name)} ({QuoteIdentifier(columnName)});";
    }

    private static bool TryGetIndexSuggestionArguments(
        JsonElement parameters,
        out string tableName,
        out string columnName)
    {
        tableName = "";
        columnName = "";

        if (!TryGetStringArguments(parameters, 2, out var values))
        {
            return false;
        }

        tableName = values[0];
        columnName = values[1];

        return true;
    }

    private static string QuoteObjectName(string objectName)
    {
        return string.Join(
            ".",
            objectName.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(QuoteIdentifier));
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
