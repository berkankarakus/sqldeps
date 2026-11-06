using System.Text.Json;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class JsonRpcRequestParserTests
{
    [Fact]
    public void TryRead_ReturnsMethodParametersAndIdForRequest()
    {
        using JsonDocument request = JsonDocument.Parse(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"initialize\",\"params\":{\"rootUri\":null}}"
        );

        var isValid = JsonRpcRequestParser.TryRead(
            request.RootElement,
            out var method,
            out var parameters,
            out var hasId,
            out var id);

        Assert.True(isValid);
        Assert.Equal("initialize", method);
        Assert.Equal(JsonValueKind.Object, parameters.ValueKind);
        Assert.True(hasId);
        Assert.Equal(7, id.GetInt32());
    }

    [Fact]
    public void TryRead_AcceptsNotificationWithoutIdOrParameters()
    {
        using JsonDocument request = JsonDocument.Parse(
            "{\"jsonrpc\":\"2.0\",\"method\":\"initialized\"}"
        );

        var isValid = JsonRpcRequestParser.TryRead(
            request.RootElement,
            out var method,
            out var parameters,
            out var hasId,
            out _);

        Assert.True(isValid);
        Assert.Equal("initialized", method);
        Assert.Equal(JsonValueKind.Undefined, parameters.ValueKind);
        Assert.False(hasId);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"method\":\"initialize\"}")]
    [InlineData("{\"jsonrpc\":2,\"method\":\"initialize\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":7}")]
    public void TryRead_RejectsMalformedRequestEnvelope(string json)
    {
        using JsonDocument request = JsonDocument.Parse(json);

        var isValid = JsonRpcRequestParser.TryRead(
            request.RootElement,
            out var method,
            out _,
            out var hasId,
            out _);

        Assert.False(isValid);
        Assert.Null(method);
        Assert.False(hasId);
    }
}
