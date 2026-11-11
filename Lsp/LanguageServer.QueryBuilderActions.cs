using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Offers read-only query preview commands for selected indexed tables and views.</summary>
public sealed partial class LanguageServer
{
    private void AddQueryBuilderAction(List<object> actions, JsonElement parameters, string? text)
    {
        if (!TryGetSelectedObjectName(parameters, text, out var selectedName))
        {
            return;
        }

        var objectName = Qualify(selectedName.Trim().Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, objectName));

        if (!IsTableOrView(match.Object))
        {
            return;
        }

        var title = $"Preview SELECT for {match.Object.Name}";
        actions.Add(new
        {
            title,
            kind = "refactor",
            data = new
            {
                source = "sqldeps",
                confidence = "high",
                experimental = true,
                rule = "query-builder",
                writesFile = false,
                applicability = "The selected text must resolve to one indexed table or view.",
                references = new[]
                {
                    new
                    {
                        title = "Microsoft Learn: SELECT",
                        summary = "SELECT defines the result set returned by a query.",
                        url = "https://learn.microsoft.com/en-us/sql/t-sql/queries/select-transact-sql?view=sql-server-ver17",
                    },
                },
            },
            command = new
            {
                title,
                command = "sqldeps.query-builder",
                arguments = new[] { match.Object.Name },
            },
        });
    }

    /// <summary>Reads the selected object name; an empty selection uses the identifier under the cursor.</summary>
    private static bool TryGetSelectedObjectName(JsonElement parameters, string? text, out string selectedName)
    {
        selectedName = "";

        if (text is null)
        {
            return false;
        }

        if (!parameters.TryGetProperty("range", out var range))
        {
            return false;
        }

        if (!TryGetRange(range, out var selectedRange))
        {
            return false;
        }

        var rawName = ReadSelectedText(text, selectedRange);

        if (rawName is null)
        {
            return false;
        }

        var normalizedName = NormalizeSelectedObjectName(rawName);

        if (normalizedName.Length == 0)
        {
            return false;
        }

        selectedName = normalizedName;

        return true;
    }

    private static string? ReadSelectedText(string text, LspRange range)
    {
        var isEmptySelection = range.Start.Line == range.End.Line
            && range.Start.Character == range.End.Character;

        if (!isEmptySelection)
        {
            return TextInRange(text, range);
        }

        return IdentifierAtCursor(text, range.Start);
    }

    private static string? IdentifierAtCursor(string text, LspPosition cursor)
    {
        return LspTextPosition.ObjectIdentifierAt(text, cursor.Line, cursor.Character);
    }

    /// <summary>Removes brackets, quotes, wrapping parentheses, call arguments and trailing separators.</summary>
    private static string NormalizeSelectedObjectName(string rawName)
    {
        var name = rawName.Trim().TrimEnd(';', ',').Trim();

        while (name.Length >= 2 && name[0] == '(' && name[^1] == ')')
        {
            name = name[1..^1].Trim();
        }

        var argumentStart = name.IndexOf('(');

        if (argumentStart >= 0)
        {
            name = name[..argumentStart].Trim();
        }

        name = name.Replace("[", "").Replace("]", "").Replace("\"", "");

        return name.Trim('.');
    }
}
