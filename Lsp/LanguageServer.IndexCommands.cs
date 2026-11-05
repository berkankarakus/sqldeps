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

        if (table is null || table.Kind is not ("table" or "view"))
        {
            throw new ArgumentException($"No indexed table or view: {tableName}");
        }

        var columnExists = AllTableColumns().Any(column =>
            SameName(column.TableName, table.Name)
            && SameName(column.ColumnName, columnName));

        if (!columnExists)
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

        if (!parameters.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Array
            || arguments.GetArrayLength() != 2)
        {
            return false;
        }

        if (arguments[0].ValueKind != JsonValueKind.String
            || arguments[1].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        tableName = arguments[0].GetString() ?? "";
        columnName = arguments[1].GetString() ?? "";

        return !string.IsNullOrWhiteSpace(tableName)
            && !string.IsNullOrWhiteSpace(columnName);
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
