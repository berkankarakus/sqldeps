using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Offers read-only query preview commands for selected indexed tables and views.</summary>
public sealed partial class LanguageServer
{
    private void AddQueryBuilderAction(List<object> actions, JsonElement parameters, string? text)
    {

        if (text is null
            || !parameters.TryGetProperty("range", out var range)
            || !TryGetRange(range, out var selectedRange)
            || TextInRange(text, selectedRange) is not { } selectedName)
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
            command = new
            {
                title,
                command = "sqldeps.query-builder",
                arguments = new[] { match.Object.Name },
            },
        });

    }
}
