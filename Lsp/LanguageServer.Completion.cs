using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Builds completion items for SQL objects and columns.</summary>
public sealed partial class LanguageServer
{
    private (string? Alias, string? Prefix) CompletionContext(JsonElement parameters)
    {
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            return (null, null);
        }

        var lines = document.Text.Split('\n');

        if (line < 0 || line >= lines.Length)
        {
            return (null, null);
        }

        var source = lines[line].TrimEnd('\r');
        var beforeCursor = source[..Math.Min(character, source.Length)];
        var match = AliasCompletionPattern().Match(beforeCursor);

        return match.Success
            ? (match.Groups["alias"].Value.Trim('[', ']'), match.Groups["prefix"].Value.Trim('[', ']'))
            : (null, null);
    }

    private string ObjectCompletionPrefix(JsonElement parameters)
    {
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            return "";
        }

        var lines = document.Text.Split('\n');

        if (line < 0 || line >= lines.Length)
        {
            return "";
        }

        var source = lines[line].TrimEnd('\r');
        var beforeCursor = source[..Math.Min(character, source.Length)];
        var match = ObjectCompletionPattern().Match(beforeCursor);

        return match.Success ? match.Groups["name"].Value.Replace("[", "").Replace("]", "") : "";
    }

    private object Completion(JsonElement parameters)
    {
        var (alias, prefix) = CompletionContext(parameters);

        if (alias is not null && prefix is not null)
        {
            var columns = ColumnCompletion(parameters, alias, prefix);

            if (columns is not null)
            {
                return columns;
            }
        }

        return ObjectCompletion(parameters);
    }

    private object? ColumnCompletion(JsonElement parameters, string alias, string prefix)
    {
        var localColumns = LocalAliasCompletion(parameters, alias, prefix);

        if (localColumns is not null)
        {
            return localColumns;
        }

        if (!TryResolveAlias(parameters, alias, out var table))
        {
            return null;
        }

        var items = AllTableColumns()
            .Where(column => SameName(column.TableName, table))
            .Where(column => column.ColumnName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(column => new { label = column.ColumnName, kind = 5, detail = column.DataType, documentation = new { kind = "markdown", value = $"`{column.TableName}.{column.ColumnName}`" } })
            .ToArray();

        return new { isIncomplete = false, items };
    }

    private object ObjectCompletion(JsonElement parameters)
    {
        var prefix = ObjectCompletionPrefix(parameters);
        var qualifiedPrefix = prefix.Contains('.') ? prefix : Qualify(prefix);
        var items = AllObjects()
            .Where(item => item.Object.Name.StartsWith(qualifiedPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Object.Name, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(item => new { label = item.Object.Name, kind = SymbolKind(item.Object.Kind), detail = item.Object.Kind, documentation = new { kind = "markdown", value = $"**{item.Object.Kind}** `{item.Object.Name}`\n\nDefined in `{item.Object.SourceFile}:{item.Object.Line}`" } })
            .ToArray();

        return new { isIncomplete = false, items };
    }

    private object? LocalAliasCompletion(JsonElement parameters, string alias, string prefix)
    {
        var (document, line, _) = DocumentAt(parameters);

        if (document is null)
        {
            return null;
        }

        var items = document.LocalColumns
            .Where(column => SameName(column.Alias, alias))
            .Where(column => column.Line <= line + 1)
            .Where(column => column.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(column => column.Line).First())
            .OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Select(column => new { label = column.Name, kind = 5, detail = column.Kind, documentation = new { kind = "markdown", value = $"`{column.Alias}.{column.Name}` ({column.Kind})" } })
            .ToArray();

        return items.Length == 0 ? null : new { isIncomplete = false, items };
    }
}
