using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddDependencyQueryActions(
        List<object> actions,
        JsonElement parameters,
        string? text)
    {
        if (TryGetSelectedIndexedObject(parameters, text, out var objectName))
        {
            AddDependencyAction(
                actions,
                $"Show who writes {objectName}",
                "sqldeps.who-writes",
                [objectName],
                "The selected text must resolve to one indexed object.");
            AddDependencyAction(
                actions,
                $"Show who reads {objectName}",
                "sqldeps.who-reads",
                [objectName],
                "The selected text must resolve to one indexed object.");
            AddDependencyAction(
                actions,
                $"Show impact of changing {objectName}",
                "sqldeps.impact",
                [objectName],
                "The selected text must resolve to one indexed object.");
            AddDependencyAction(
                actions,
                $"Show reverse callers of {objectName}",
                "sqldeps.callers",
                [objectName],
                "The selected text must resolve to one indexed object.");
        }

        if (TryGetSelectedColumn(parameters, text, out var tableName, out var columnName))
        {
            AddDependencyAction(
                actions,
                $"Show column writers for {tableName}.{columnName}",
                "sqldeps.column-writers",
                [tableName, columnName],
                "The selected text must resolve to one indexed table column.");
        }
    }

    private void AddDependencyAction(
        List<object> actions,
        string title,
        string command,
        string[] arguments,
        string applicability)
    {
        actions.Add(new
        {
            title,
            kind = "source",
            data = new
            {
                source = "sqldeps",
                confidence = "high",
                writesFile = false,
                applicability,
                generation = "dependency-context",
                rule = command["sqldeps.".Length..],
            },
            command = new
            {
                title,
                command,
                arguments,
            },
        });
    }

    private bool TryGetSelectedIndexedObject(
        JsonElement parameters,
        string? text,
        out string objectName)
    {
        objectName = "";

        if (!TryGetSelectedObjectName(parameters, text, out var selectedName))
        {
            return false;
        }

        var qualifiedName = Qualify(selectedName.Trim().Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, qualifiedName));

        if (match.Object is null)
        {
            return false;
        }

        objectName = match.Object.Name;

        return true;
    }

    private bool TryGetSelectedColumn(
        JsonElement parameters,
        string? text,
        out string tableName,
        out string columnName)
    {
        tableName = "";
        columnName = "";

        if (!TryGetSelectedObjectName(parameters, text, out var selectedName))
        {
            return false;
        }

        string[] parts = selectedName
            .Trim()
            .Replace("[", "")
            .Replace("]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        string selectedTable = string.Join('.', parts[..^1]);
        string selectedColumn = parts[^1];
        string qualifiedTable = Qualify(selectedTable);
        var table = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, qualifiedTable));

        if (!IsTableOrView(table.Object))
        {
            return false;
        }

        bool columnExists = AllTableColumns().Any(column =>
            SameName(column.TableName, table.Object.Name)
            && SameName(column.ColumnName, selectedColumn));

        if (!columnExists)
        {
            return false;
        }

        tableName = table.Object.Name;
        columnName = selectedColumn;

        return true;
    }
}
