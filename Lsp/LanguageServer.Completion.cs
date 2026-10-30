using System.Text.Json;
using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

/// <summary>Builds completion items for SQL objects and columns.</summary>
public sealed partial class LanguageServer
{
    private bool TryResolveAlias(JsonElement parameters, string alias, out string table)
    {
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            table = "";

            return false;
        }

        var beforeCursor = string.Join('\n', document.Text.Split('\n').Take(line + 1));

        if (TryResolveAlias(beforeCursor, alias, out table))
        {
            return true;
        }

        return TryResolveSelectListAlias(document.Text, line, character, alias, out table);
    }

    private static bool TryResolveAlias(string text, string alias, out string table)
    {
        foreach (Match match in TableAliasPattern().Matches(text).Reverse())
        {
            var writtenAlias = match.Groups["alias"].Success ? match.Groups["alias"].Value : match.Groups["table"].Value.Split('.').Last().Trim('[', ']');

            if (string.Equals(writtenAlias, alias, StringComparison.OrdinalIgnoreCase))
            {
                table = Qualify(match.Groups["table"].Value.Replace("[", "").Replace("]", ""));

                return true;
            }
        }

        table = "";

        return false;
    }

    private static bool TryResolveSelectListAlias(string text, int line, int character, string alias, out string table)
    {
        var cursor = OffsetAt(text, line, character);
        var selectMatches = SelectPattern().Matches(text[..cursor]);
        var currentSelect = selectMatches.Count == 0 ? null : selectMatches[^1];
        var nextFrom = FromPattern().Match(text, cursor);

        if (currentSelect is null || !nextFrom.Success || FromPattern().IsMatch(text[currentSelect.Index..cursor]))
        {
            table = "";

            return false;
        }

        var nextSelect = SelectPattern().Match(text, nextFrom.Index + nextFrom.Length);
        var statementEnd = text.IndexOf(';', nextFrom.Index);
        var end = new[] { nextSelect.Success ? nextSelect.Index : text.Length, statementEnd >= 0 ? statementEnd : text.Length }.Min();

        return TryResolveAlias(text[nextFrom.Index..end], alias, out table);
    }

    private static int OffsetAt(string text, int line, int character)
    {
        var lines = text.Split('\n');
        var precedingLength = lines.Take(Math.Clamp(line, 0, lines.Length)).Sum(item => item.Length + 1);
        var currentLine = line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : "";

        return Math.Min(precedingLength + Math.Max(character, 0), precedingLength + currentLine.Length);
    }

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
        if (InsertColumnCompletion(parameters) is { } insertColumns)
        {
            return insertColumns;
        }

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

    private object? InsertColumnCompletion(JsonElement parameters)
    {
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            return null;
        }

        var cursor = OffsetAt(document.Text, line, character);
        var match = InsertColumnPattern().Match(document.Text[..cursor]);

        if (!match.Success)
        {
            return null;
        }

        var table = Qualify(match.Groups["table"].Value);
        var prefix = match.Groups["columns"].Value.Split(',').Last().Trim().Trim('[', ']');
        var items = AllTableColumns()
            .Where(column => SameName(column.TableName, table))
            .Where(column => column.ColumnName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(column => new { label = column.ColumnName, kind = 5, detail = column.DataType })
            .ToArray();

        return items.Length == 0 ? null : new { isIncomplete = false, items };
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
            .Where(column => !column.IsDrop)
            .Where(column => column.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(column => column.Line).First())
            .OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Select(column => new { label = column.Name, kind = 5, detail = column.Kind, documentation = new { kind = "markdown", value = $"`{column.Alias}.{column.Name}` ({column.Kind})" } })
            .ToArray();

        return items.Length == 0 ? null : new { isIncomplete = false, items };
    }

    [GeneratedRegex(@"\bINSERT\s+INTO\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})\s*\(\s*(?<columns>[^)]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex InsertColumnPattern();
}
