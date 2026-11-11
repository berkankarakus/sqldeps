using System.Text.Json;

namespace SqlDeps.Lsp;

internal static class DocumentChangeApplier
{
    public static string Apply(
        string currentText,
        JsonElement changes)
    {
        var text = currentText;

        foreach (var change in changes.EnumerateArray())
        {
            var replacement = ChangeText(change);

            if (!change.TryGetProperty("range", out var range))
            {
                text = replacement;

                continue;
            }

            text = ApplyRangedChange(text, range, replacement);
        }

        return text;
    }

    private static string ChangeText(JsonElement change)
    {
        if (!change.TryGetProperty("text", out var text))
        {
            throw new InvalidOperationException("Document changes must contain text.");
        }

        if (text.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Document changes must contain text.");
        }

        var replacement = text.GetString();

        if (replacement is null)
        {
            throw new InvalidOperationException("Document change text cannot be null.");
        }

        return replacement;
    }

    private static string ApplyRangedChange(
        string text,
        JsonElement range,
        string replacement)
    {
        var start = ChangeOffset(text, range.GetProperty("start"));
        var end = ChangeOffset(text, range.GetProperty("end"));

        if (start > end)
        {
            throw new InvalidOperationException("Document change range is reversed.");
        }

        return text[..start] + replacement + text[end..];
    }

    private static int ChangeOffset(
        string text,
        JsonElement position)
    {
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();

        return LspTextPosition.OffsetAt(text, line, character);
    }
}
