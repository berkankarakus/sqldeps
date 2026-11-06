namespace SqlDeps.Lsp;

internal readonly record struct SemanticToken(
    int Line,
    int Start,
    int Length,
    int Type
);

internal static class SemanticTokenEncoder
{
    public static int[] Encode(IEnumerable<SemanticToken> tokens)
    {
        var ordered = tokens
            .OrderBy(token => token.Line)
            .ThenBy(token => token.Start)
            .ToArray();
        var encoded = new List<int>(ordered.Length * 5);
        var previousLine = 0;
        var previousStart = 0;

        foreach (var token in ordered)
        {
            var deltaLine = token.Line - previousLine;
            encoded.Add(deltaLine);
            encoded.Add(deltaLine == 0 ? token.Start - previousStart : token.Start);
            encoded.Add(token.Length);
            encoded.Add(token.Type);
            encoded.Add(0);
            previousLine = token.Line;
            previousStart = token.Start;
        }

        return encoded.ToArray();
    }
}
