using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private bool TryGetColumnDefinition(JsonElement parameters, out TableColumn column)
    {
        column = null!;

        if (!TryGetColumnReference(parameters, out var tableName, out var columnName))
        {
            return false;
        }

        var matches = AllTableColumns()
            .Where(candidate => SameName(candidate.TableName, tableName)
                && SameName(candidate.ColumnName, columnName))
            .DistinctBy(candidate => (candidate.TableName, candidate.ColumnName, candidate.SourceFile, candidate.Line))
            .ToArray();

        if (matches.Length != 1)
        {
            return false;
        }

        column = matches[0];

        return true;
    }

    private bool TryGetColumnReference(JsonElement parameters, out string tableName, out string columnName)
    {
        tableName = "";
        columnName = "";

        var (document, line, character) = DocumentAt(parameters);
        if (document is null)
        {
            return false;
        }

        var identifier = IdentifierAt(document.Text, line, character);
        if (identifier is null)
        {
            return false;
        }

        var parts = identifier
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('[', ']'))
            .ToArray();
        if (parts.Length == 0)
        {
            return false;
        }

        columnName = parts[^1];
        if (columnName.Length == 0)
        {
            return false;
        }

        var resolvedColumnName = columnName;

        if (parts.Length >= 2)
        {
            var qualifier = parts[^2];
            if (TryResolveAlias(parameters, qualifier, out tableName))
            {
                return true;
            }

            if (parts.Length == 3)
            {
                tableName = Qualify(parts[0] + "." + parts[1]);

                return true;
            }

            return false;
        }

        var usageTables = document.ColumnUsage
            .Where(usage => usage.Line == line + 1 && SameName(usage.ColumnName, resolvedColumnName))
            .Select(usage => usage.TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (usageTables.Length == 1)
        {
            tableName = usageTables[0];

            return true;
        }

        var visibleTables = TableAliasPattern()
            .Matches(TextBefore(document.Text, line, character))
            .Select(QualifiedTable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visibleMatches = AllTableColumns()
            .Where(column => visibleTables.Contains(column.TableName, StringComparer.OrdinalIgnoreCase)
                && SameName(column.ColumnName, resolvedColumnName))
            .Select(column => column.TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (visibleMatches.Length == 1)
        {
            tableName = visibleMatches[0];

            return true;
        }

        var globalMatches = AllTableColumns()
            .Where(column => SameName(column.ColumnName, resolvedColumnName))
            .Select(column => column.TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (globalMatches.Length != 1)
        {
            return false;
        }

        tableName = globalMatches[0];

        return true;
    }

    private static string? IdentifierAt(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var source = lines[line].TrimEnd('\r');
        var index = Math.Min(Math.Max(character, 0), source.Length);
        var start = index;
        var end = index;

        while (start > 0 && IsNameCharacter(source[start - 1]))
        {
            start--;
        }

        while (end < source.Length && IsNameCharacter(source[end]))
        {
            end++;
        }

        var identifier = source[start..end].Replace("[", "").Replace("]", "");

        return identifier.Length == 0 ? null : identifier;
    }

    private static string TextBefore(string text, int line, int character)
    {
        var lines = text.Split('\n');
        var precedingLines = lines.Take(Math.Clamp(line, 0, lines.Length));
        var prefix = string.Join('\n', precedingLines);

        if (line < 0 || line >= lines.Length)
        {
            return prefix;
        }

        return prefix.Length == 0
            ? lines[line][..Math.Min(Math.Max(character, 0), lines[line].Length)]
            : prefix + '\n' + lines[line][..Math.Min(Math.Max(character, 0), lines[line].Length)];
    }

    private static bool SameColumn(ColumnUsage usage, TableColumn column)
    {
        return SameName(usage.TableName, column.TableName)
            && SameName(usage.ColumnName, column.ColumnName);
    }
}
