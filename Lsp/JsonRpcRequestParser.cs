using System.Text.Json;

namespace SqlDeps.Lsp;

internal static class JsonRpcRequestParser
{
    public static bool TryRead(
        JsonElement request,
        out string? method,
        out JsonElement parameters,
        out bool hasId,
        out JsonElement id)
    {
        method = null;
        parameters = default;
        hasId = false;
        id = default;

        if (request.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!request.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String
            || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal))
        {
            return false;
        }

        if (!request.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        method = methodElement.GetString();
        parameters = request.TryGetProperty("params", out var value) ? value : default;
        hasId = request.TryGetProperty("id", out id);

        return true;
    }
}
