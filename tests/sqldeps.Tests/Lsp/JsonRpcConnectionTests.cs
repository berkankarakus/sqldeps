using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class JsonRpcConnectionTests
{
    [Fact]
    public void Read_UsesUtf8ByteLengthAndIgnoresAdditionalHeaders()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"params\":{\"text\":\"ç\"}}";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] headerBytes = Encoding.ASCII.GetBytes(
            $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\n\r\n"
        );
        byte[] framedMessage = headerBytes.Concat(bodyBytes).ToArray();

        using MemoryStream input = new(framedMessage);
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        using JsonDocument? message = connection.Read();

        Assert.NotNull(message);
        Assert.Equal("test", message.RootElement.GetProperty("method").GetString());
        Assert.Equal("ç", message.RootElement.GetProperty("params").GetProperty("text").GetString());
    }

    [Fact]
    public void Read_PreservesFrameBoundariesAcrossConsecutiveMessages()
    {
        const string firstBody = "{\"jsonrpc\":\"2.0\",\"method\":\"first\"}";
        const string secondBody = "{\"jsonrpc\":\"2.0\",\"method\":\"second\"}";
        byte[] inputBytes = CreateFrame(firstBody)
            .Concat(CreateFrame(secondBody))
            .ToArray();

        using MemoryStream input = new(inputBytes);
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        using JsonDocument? firstMessage = connection.Read();
        using JsonDocument? secondMessage = connection.Read();

        Assert.Equal("first", firstMessage?.RootElement.GetProperty("method").GetString());
        Assert.Equal("second", secondMessage?.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void Read_RejectsDuplicateContentLengthHeaders()
    {
        using MemoryStream input = new(
            Encoding.ASCII.GetBytes("Content-Length: 0\r\nContent-Length: 0\r\n\r\n")
        );
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<InvalidDataException>(() => connection.Read());

        Assert.Contains("duplicate Content-Length", exception.Message);
    }

    [Fact]
    public void Read_RejectsTruncatedJsonBody()
    {
        using MemoryStream input = new(
            Encoding.ASCII.GetBytes("Content-Length: 10\r\n\r\n{}")
        );
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<EndOfStreamException>(() => connection.Read());

        Assert.Contains("ended before its JSON body", exception.Message);
    }

    [Fact]
    public void Read_RejectsMissingContentLengthHeader()
    {
        using MemoryStream input = new(Encoding.ASCII.GetBytes("Content-Type: application/json\r\n\r\n{}"));
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<InvalidDataException>(() => connection.Read());

        Assert.Contains("no Content-Length header", exception.Message);
    }

    [Fact]
    public void Read_RejectsContentLengthAboveTransportLimit()
    {
        using MemoryStream input = new(
            Encoding.ASCII.GetBytes("Content-Length: 67108865\r\n\r\n"));
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<InvalidDataException>(() => connection.Read());

        Assert.Contains("exceeds the 67108864 byte limit", exception.Message);
    }

    [Fact]
    public void Read_RejectsInvalidContentLengthHeader()
    {
        using MemoryStream input = new(
            Encoding.ASCII.GetBytes("Content-Length: nope\r\n\r\n"));
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<InvalidDataException>(() => connection.Read());

        Assert.Contains("invalid Content-Length header", exception.Message);
    }

    [Fact]
    public void Read_RejectsHeadersThatExceedTransportLimit()
    {
        string header = "X-Header: " + new string('x', 8192) + "\r\n\r\n";
        using MemoryStream input = new(Encoding.ASCII.GetBytes(header));
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        var exception = Assert.Throws<InvalidDataException>(() => connection.Read());

        Assert.Contains("header exceeds the 8192 byte limit", exception.Message);
    }

    [Fact]
    public void Read_RejectsMalformedJsonBody()
    {
        using MemoryStream input = new(CreateFrame("{"));
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        Assert.ThrowsAny<JsonException>(() => connection.Read());
    }

    [Fact]
    public void Notify_WritesJsonRpcNotificationWithByteContentLength()
    {
        using MemoryStream input = new();
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);

        connection.Notify(
            "textDocument/publishDiagnostics",
            new { uri = "file:///test.sql", message = "ç" }
        );

        using JsonDocument message = ReadWrittenMessage(output);

        Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(
            "textDocument/publishDiagnostics",
            message.RootElement.GetProperty("method").GetString());
        Assert.Equal("ç", message.RootElement.GetProperty("params").GetProperty("message").GetString());
    }

    [Fact]
    public void RespondError_WritesIdAndErrorPayload()
    {
        using MemoryStream input = new();
        using MemoryStream output = new();
        JsonRpcConnection connection = new(input, output);
        using JsonDocument request = JsonDocument.Parse("{\"id\":7}");

        connection.RespondError(request.RootElement.GetProperty("id"), -32602, "Invalid request parameters.");

        using JsonDocument message = ReadWrittenMessage(output);
        var error = message.RootElement.GetProperty("error");

        Assert.Equal(7, message.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal("Invalid request parameters.", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(32)]
    [InlineData((int)SocketError.ConnectionReset)]
    public void IsClientDisconnect_RecognizesSocketDisconnects(int errorCode)
    {
        var exception = new IOException("client disconnected", new SocketException(errorCode));

        Assert.True(LanguageServer.IsClientDisconnect(exception));
    }

    [Fact]
    public void IsClientDisconnect_RejectsUnrelatedIoErrors()
    {
        var exception = new IOException("disk failure");

        Assert.False(LanguageServer.IsClientDisconnect(exception));
    }

    private static JsonDocument ReadWrittenMessage(MemoryStream output)
    {
        byte[] bytes = output.ToArray();
        string framedMessage = Encoding.ASCII.GetString(bytes);
        int separatorIndex = framedMessage.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        Assert.True(separatorIndex >= 0);

        int bodyStart = separatorIndex + 4;
        int contentLength = ParseContentLength(framedMessage[..separatorIndex]);
        byte[] body = bytes[bodyStart..];

        Assert.Equal(contentLength, body.Length);

        return JsonDocument.Parse(body);
    }

    private static byte[] CreateFrame(string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");

        return headerBytes.Concat(bodyBytes).ToArray();
    }

    private static int ParseContentLength(string headers)
    {
        string contentLengthHeader = headers
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(header => header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        return int.Parse(contentLengthHeader["Content-Length:".Length..].Trim());
    }
}
