using System.Text.Json;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class DocumentChangeApplierTests
{
    [Fact]
    public void Apply_ReplacesWholeDocumentWhenChangeHasNoRange()
    {
        using JsonDocument changes = JsonDocument.Parse("[{\"text\":\"SELECT 2;\"}]");

        var result = DocumentChangeApplier.Apply("SELECT 1;", changes.RootElement);

        Assert.Equal("SELECT 2;", result);
    }

    [Fact]
    public void Apply_AppliesRangedChangesSequentially()
    {
        using JsonDocument changes = JsonDocument.Parse(
            "[{\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":1}},\"text\":\"A\"},{\"range\":{\"start\":{\"line\":1,\"character\":0},\"end\":{\"line\":1,\"character\":1}},\"text\":\"B\"}]"
        );

        var result = DocumentChangeApplier.Apply("x\ny", changes.RootElement);

        Assert.Equal("A\nB", result);
    }

    [Fact]
    public void Apply_UsesUtf16CharacterOffsetsForNonBmpCharacters()
    {
        using JsonDocument changes = JsonDocument.Parse(
            "[{\"range\":{\"start\":{\"line\":0,\"character\":3},\"end\":{\"line\":0,\"character\":4}},\"text\":\"B\"}]"
        );

        var result = DocumentChangeApplier.Apply("a😀b", changes.RootElement);

        Assert.Equal("a😀B", result);
    }

    [Fact]
    public void Apply_UsesLogicalLineEndForCrLfRanges()
    {
        using JsonDocument changes = JsonDocument.Parse(
            "[{\"range\":{\"start\":{\"line\":0,\"character\":1},\"end\":{\"line\":0,\"character\":1}},\"text\":\"X\"}]"
        );

        var result = DocumentChangeApplier.Apply("a\r\nb", changes.RootElement);

        Assert.Equal("aX\r\nb", result);
    }

    [Fact]
    public void Apply_RejectsReversedRanges()
    {
        using JsonDocument changes = JsonDocument.Parse(
            "[{\"range\":{\"start\":{\"line\":0,\"character\":2},\"end\":{\"line\":0,\"character\":1}},\"text\":\"x\"}]"
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DocumentChangeApplier.Apply("abc", changes.RootElement));

        Assert.Contains("range is reversed", exception.Message);
    }

    [Fact]
    public void Apply_RejectsChangesWithoutText()
    {
        using JsonDocument changes = JsonDocument.Parse("[{\"range\":{}}]");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DocumentChangeApplier.Apply("abc", changes.RootElement));

        Assert.Contains("must contain text", exception.Message);
    }
}
