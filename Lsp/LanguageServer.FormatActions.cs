using SqlDeps.Core;

namespace SqlDeps.Lsp;

/// <summary>Offers a parse-validated, whole-script formatting code action.</summary>
public sealed partial class LanguageServer
{
    private void AddFormatAction(
        List<object> actions,
        string uri,
        string? text,
        IReadOnlyList<LintDiagnostic> diagnostics)
    {
        if (text is null)
        {
            return;
        }

        // Fail closed like the other edit actions: no text edits while the index is stale.
        if (diagnostics.Any(IsStaleDiagnostic))
        {
            return;
        }

        var result = SqlFormatter.Format(text);

        if (!result.IsSuccess)
        {
            return;
        }

        if (string.Equals(result.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var range = new LspRange(
            new LspPosition(0, 0),
            PositionAt(text, text.Length).Start);
        var title = "Format SQL script";

        actions.Add(new
        {
            title,
            kind = "source.format",
            data = ReplacementActionData(
                title,
                uri,
                "high",
                [(range, result.Text)],
                applicability: "The whole script parses; comments are preserved and only layout is rewritten."),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [uri] = new[] { new { range, newText = result.Text } },
                },
            },
        });
    }
}
