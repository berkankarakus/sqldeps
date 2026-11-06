using System.Text.Json;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class LspDocumentVersionTests
{
    [Fact]
    public void Read_ReturnsInt32DocumentVersion()
    {
        using JsonDocument document = JsonDocument.Parse("{\"version\":12}");

        Assert.Equal(12, LspDocumentVersion.Read(document.RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":1.5}")]
    [InlineData("{\"version\":\"12\"}")]
    [InlineData("{\"version\":2147483648}")]
    public void Read_ReturnsNullForMissingOrInvalidDocumentVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Null(LspDocumentVersion.Read(document.RootElement));
    }
}
