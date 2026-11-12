using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class LspTextPositionTests
{
    [Fact]
    public void OffsetAt_ClampsToTheCurrentLine()
    {
        var offset = LspTextPosition.OffsetAt("abc\ndef", 1, 99);

        Assert.Equal(7, offset);
    }

    [Fact]
    public void PositionAt_ReturnsZeroBasedUtf16Position()
    {
        var position = LspTextPosition.PositionAt("a\n😀b", 4).Start;

        Assert.Equal(1, position.Line);
        Assert.Equal(2, position.Character);
    }

    [Fact]
    public void PositionAt_ClampsCrLfLineEndToTheLogicalLineLength()
    {
        var beforeLineFeed = LspTextPosition.PositionAt("a\r\nb", 2).Start;
        var nextLine = LspTextPosition.PositionAt("a\r\nb", 3).Start;

        Assert.Equal(0, beforeLineFeed.Line);
        Assert.Equal(1, beforeLineFeed.Character);
        Assert.Equal(1, nextLine.Line);
        Assert.Equal(0, nextLine.Character);
    }

    [Fact]
    public void ColumnIdentifierAt_PreservesTheExistingClampedCursorBehavior()
    {
        var identifier = LspTextPosition.ColumnIdentifierAt("[dbo].[Orders]", 0, 99);

        Assert.Equal("dbo.Orders", identifier);
    }

    [Fact]
    public void ObjectIdentifierAt_PreservesObjectNameCharacters()
    {
        var identifier = LspTextPosition.ObjectIdentifierAt("EXEC dbo.@Proc$1", 0, 14);

        Assert.Equal("dbo.@Proc$1", identifier);
    }
}
