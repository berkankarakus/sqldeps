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
            return [];
        }

        var result = SqlFormatter.Format(document.Text);

        if (!result.IsSuccess || string.Equals(result.Text, document.Text, StringComparison.Ordinal))
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
