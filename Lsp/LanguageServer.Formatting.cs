using System.Text.Json;
using SqlDeps.Core;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object[] Formatting(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var document = OpenDocument(uri);

        if (document is null)
        {
            throw new LspRequestFailedException("The document is not open in the sqldeps language server.");
        }

        var result = SqlFormatter.Format(document.Text);

        if (!result.IsSuccess)
        {
            var reason = result.Errors.Count > 0 ? result.Errors[0] : "The script could not be parsed.";

            throw new LspRequestFailedException($"The script cannot be formatted: {reason}");
        }

        if (string.Equals(result.Text, document.Text, StringComparison.Ordinal))
        {
            return [];
        }

        var end = PositionAt(document.Text, document.Text.Length).Start;

        return
        [
            new
            {
                range = new LspRange(
                    new LspPosition(0, 0),
                    end),
                newText = result.Text,
            },
        ];
    }
}
