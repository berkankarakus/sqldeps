using System.Text.Json;

namespace SqlDeps.Core;

internal static class RefactorWorkspaceEditReader
{
    internal static bool TryRead(
        JsonElement edit,
        out RefactorWorkspaceEdit workspaceEdit
    )
    {
        workspaceEdit = null!;

        if (edit.ValueKind != JsonValueKind.Object
            || !edit.TryGetProperty("changes", out var changes)
            || changes.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var fileChanges = new Dictionary<string, IReadOnlyList<RefactorTextEdit>>(StringComparer.Ordinal);

        foreach (var file in changes.EnumerateObject())
        {
            if (!TryGetFileEdits(file.Value, out IReadOnlyList<RefactorTextEdit> edits))
            {
                return false;
            }

            fileChanges[file.Name] = edits;
        }

        if (fileChanges.Count == 0 || fileChanges.Count != 1)
        {
            return false;
        }

        workspaceEdit = new RefactorWorkspaceEdit(fileChanges);

        return true;
    }

    private static bool TryGetFileEdits(
        JsonElement fileChanges,
        out IReadOnlyList<RefactorTextEdit> edits
    )
    {
        edits = [];

        if (fileChanges.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<RefactorTextEdit> parsedEdits = [];

        foreach (JsonElement change in fileChanges.EnumerateArray())
        {
            if (!TryGetTextEdit(change, out RefactorTextEdit textEdit))
            {
                return false;
            }

            parsedEdits.Add(textEdit);
        }

        if (parsedEdits.Count == 0)
        {
            return false;
        }

        edits = parsedEdits;

        return true;
    }

    private static bool TryGetTextEdit(
        JsonElement value,
        out RefactorTextEdit textEdit
    )
    {
        textEdit = null!;

        if (!value.TryGetProperty("range", out var range)
            || !TryGetRange(range, out var parsedRange))
        {
            return false;
        }

        if (!value.TryGetProperty("newText", out var newText)
            || newText.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        textEdit = new RefactorTextEdit(parsedRange, newText.GetString()!);

        return true;
    }

    private static bool TryGetRange(
        JsonElement value,
        out RefactorRange range
    )
    {
        range = null!;

        if (!value.TryGetProperty("start", out var start)
            || !TryGetPosition(start, out var parsedStart))
        {
            return false;
        }

        if (!value.TryGetProperty("end", out var end)
            || !TryGetPosition(end, out var parsedEnd))
        {
            return false;
        }

        range = new RefactorRange(parsedStart, parsedEnd);

        return true;
    }

    private static bool TryGetPosition(
        JsonElement value,
        out RefactorPosition position
    )
    {
        position = null!;

        if (!value.TryGetProperty("line", out var line)
            || !line.TryGetInt32(out var lineValue)
            || !value.TryGetProperty("character", out var character)
            || !character.TryGetInt32(out var characterValue)
            || lineValue < 0
            || characterValue < 0)
        {
            return false;
        }

        position = new RefactorPosition(lineValue, characterValue);

        return true;
    }
}
