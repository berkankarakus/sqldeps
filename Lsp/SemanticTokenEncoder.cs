namespace SqlDeps.Lsp;

internal readonly record struct SemanticToken(
    int Line,
    int Start,
    int Length,
    int Type
);

internal static class SemanticTokenEncoder
{
    private const int EncodedFieldsPerToken = 5;
    private const int NoTokenModifiers = 0;

    public static int[] Encode(IEnumerable<SemanticToken> tokens)
    {
        var ordered = tokens
            .OrderBy(token => token.Line)
            .ThenBy(token => token.Start)
            .ToArray();
        var encoded = new List<int>(ordered.Length * EncodedFieldsPerToken);
        var previousLine = 0;
        var previousStart = 0;

        foreach (var token in ordered)
        {
            var deltaLine = token.Line - previousLine;
            encoded.Add(deltaLine);
            encoded.Add(deltaLine == 0 ? token.Start - previousStart : token.Start);
            encoded.Add(token.Length);
            encoded.Add(token.Type);
            encoded.Add(NoTokenModifiers);
            previousLine = token.Line;
            previousStart = token.Start;
        }

        return encoded.ToArray();
    }
}
