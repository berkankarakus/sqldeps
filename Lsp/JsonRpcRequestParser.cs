using System.Text.Json;

namespace SqlDeps.Lsp;

internal static class JsonRpcRequestParser
{
    private const string JsonRpcVersion = "2.0";

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

        if (!request.TryGetProperty("jsonrpc", out var version))
        {
            return false;
        }

        if (version.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!string.Equals(version.GetString(), JsonRpcVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!request.TryGetProperty("method", out var methodElement))
        {
            return false;
        }

        if (methodElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        method = methodElement.GetString();
        parameters = request.TryGetProperty("params", out var value) ? value : default;
        hasId = request.TryGetProperty("id", out id);

        return true;
    }
}
