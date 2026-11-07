using System.Text.Json;

namespace SqlDeps.Lsp;

internal static class CodeActionFilter
{
    public static object[] Filter(
        IReadOnlyList<object> actions,
        JsonElement parameters)
    {
        var requestedKinds = RequestedKinds(parameters);

        if (requestedKinds.Count == 0)
        {
            return actions.ToArray();
        }

        return actions
            .Where(action => HasRequestedKind(action, requestedKinds))
            .ToArray();
    }

    private static HashSet<string> RequestedKinds(JsonElement parameters)
    {
        if (!TryGetRequestedKinds(parameters, out var only))
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

    private static bool TryGetRequestedKinds(JsonElement parameters, out JsonElement kinds)
    {
        kinds = default;

        if (!parameters.TryGetProperty("context", out var context))
        {
            return false;
        }

        if (!context.TryGetProperty("only", out kinds))
        {
            return false;
        }

        return kinds.ValueKind == JsonValueKind.Array;
    }

    private static bool HasRequestedKind(
        object action,
        IReadOnlySet<string> requestedKinds)
    {
        var actionElement = JsonSerializer.SerializeToElement(action);

        if (!actionElement.TryGetProperty("kind", out var kind))
        {
            return false;
        }

        if (kind.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var actionKind = kind.GetString();

        if (actionKind is null)
        {
            return false;
        }

        return requestedKinds.Any(requestedKind => MatchesRequestedKind(actionKind, requestedKind));
    }

    private static bool MatchesRequestedKind(string actionKind, string requestedKind)
    {
        if (string.Equals(actionKind, requestedKind, StringComparison.Ordinal))
        {
            return true;
        }

        return actionKind.StartsWith(requestedKind + ".", StringComparison.Ordinal);
    }
}
