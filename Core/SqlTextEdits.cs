namespace SqlDeps.Core;

/// <summary>Non-overlapping text replacements by offset; applying them returns null when two of them collide.</summary>
internal sealed class SqlTextEdits
{
    private readonly List<(int Start, int Length, string Text)> edits = [];

    public bool IsEmpty => edits.Count == 0;

    public void Replace(int start, int length, string text)
    {
        edits.Add((start, length, text));
    }

    public string? Apply(string source)
    {
        var ordered = edits.OrderBy(edit => edit.Start).ToList();

        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].Start < ordered[index - 1].Start + ordered[index - 1].Length)
            {
                return null;
            }
        }

        System.Text.StringBuilder result = new(source);

        for (var index = ordered.Count - 1; index >= 0; index--)
        {
            var edit = ordered[index];

            result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        }

        return result.ToString();
    }
}
