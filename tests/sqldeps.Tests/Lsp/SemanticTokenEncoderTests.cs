using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class SemanticTokenEncoderTests
{
    [Fact]
    public void Encode_SortsTokensAndUsesLspDeltaPositions()
    {
        var tokens = new[]
        {
            new SemanticToken(2, 4, 3, 1),
            new SemanticToken(0, 5, 2, 0),
            new SemanticToken(0, 9, 4, 0),
        };

        var encoded = SemanticTokenEncoder.Encode(tokens);

        Assert.Equal(
            [0, 5, 2, 0, 0, 0, 4, 4, 0, 0, 2, 4, 3, 1, 0],
            encoded
        );
    }

    [Fact]
    public void Encode_ReturnsEmptyDataForNoTokens()
    {
        var encoded = SemanticTokenEncoder.Encode([]);

        Assert.Empty(encoded);
    }

    [Fact]
    public void Encode_ProducesStableRowsForDuplicateTokens()
    {
        var tokens = new[]
        {
            new SemanticToken(1, 2, 3, 0),
            new SemanticToken(1, 2, 3, 0),
        };

        var encoded = SemanticTokenEncoder.Encode(tokens);

        Assert.Equal([1, 2, 3, 0, 0, 0, 0, 3, 0, 0], encoded);
    }
}
