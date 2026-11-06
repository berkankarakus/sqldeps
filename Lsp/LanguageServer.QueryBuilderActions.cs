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

        if (match.Object is null || match.Object.Kind is not ("table" or "view"))
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

        if (TextInRange(text, selectedRange) is not { } name)
        {
            return false;
        }

        selectedName = name;

        return true;
    }
}
