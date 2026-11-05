using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static object[] FilterCodeActions(IReadOnlyList<object> actions, JsonElement parameters)
    {
        var requestedKinds = RequestedActionKinds(parameters);


        if (requestedKinds.Count == 0)
        {

            return actions.ToArray();
        }

        return actions
            .Where(action => HasRequestedKind(action, requestedKinds))
            .ToArray();
    }

    private static HashSet<string> RequestedActionKinds(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("context", out var context)
            || !context.TryGetProperty("only", out var only)
            || only.ValueKind != JsonValueKind.Array)
        {

            return [];
        }

        return only.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .Where(kind => kind.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasRequestedKind(object action, IReadOnlySet<string> requestedKinds)
    {
        var actionElement = JsonSerializer.SerializeToElement(action);


        if (!actionElement.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String)
        {

            return false;
        }

        var actionKind = kind.GetString();


        if (actionKind is null)
        {

            return false;
        }

        return requestedKinds.Any(requestedKind =>
            string.Equals(actionKind, requestedKind, StringComparison.Ordinal)
            || actionKind.StartsWith(requestedKind + ".", StringComparison.Ordinal));
    }
}
