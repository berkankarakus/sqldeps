using System.Text.Json;

namespace SqlDeps.Lsp;

internal static class LspDocumentVersion
{
    public static int? Read(JsonElement document)
    {
        if (!document.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }
}
