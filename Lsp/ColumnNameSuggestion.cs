namespace SqlDeps.Lsp;

/// <summary>Finds a conservative spelling suggestion among known column names.</summary>
internal static class ColumnNameSuggestion
{
    public static string? For(string unknownName, IEnumerable<string> knownNames)
    {
        if (unknownName.Length < 4)
        {
            return null;
        }

        var maximumDistance = Math.Min(3, Math.Max(1, unknownName.Length / 3));
        var matches = knownNames
            .Select(name => new Candidate(name, EditDistance(unknownName, name)))
            .Where(candidate => candidate.Distance <= maximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0 || HasEquallyCloseAlternative(matches))
        {
            return null;
        }

        return matches[0].Name;
    }

    private static bool HasEquallyCloseAlternative(IReadOnlyList<Candidate> matches)
    {
        if (matches.Count < 2)
        {
            return false;
        }

        return matches[0].Distance == matches[1].Distance;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record Candidate(string Name, int Distance);
}
