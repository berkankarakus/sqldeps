using System.Text.Json;
using SqlDeps.Core;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private string ExecuteQueryBuilderCommand(JsonElement parameters)
    {
        if (!TryGetCommandObjectName(parameters, out var objectName))
        {
            throw new ArgumentException("sqldeps.query-builder requires one object name argument.");
        }

        var qualifiedName = Qualify(objectName.Trim().Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, qualifiedName));

        if (!IsTableOrView(match.Object))
        {
            throw new ArgumentException($"No indexed table or view: {qualifiedName}");
        }

        var columns = AllTableColumns()
            .Where(column => SameName(column.TableName, match.Object.Name))
            .Select(column => column.ColumnName);

        return SqlQueryBuilder.BuildSelect(
            match.Object.Name,
            match.Object.Kind,
            columns
        ).Sql;
    }

    private static bool TryGetCommandObjectName(JsonElement parameters, out string objectName)
    {
        objectName = "";

        if (!parameters.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Array
            || arguments.GetArrayLength() != 1
            || arguments[0].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = arguments[0].GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        objectName = value;

        return true;
    }
}
