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

        if (!TryGetStringArguments(parameters, 1, out var values))
        {
            return false;
        }

        objectName = values[0];

        return true;
    }
}
