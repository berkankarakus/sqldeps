using System.Text;
using System.Text.Json;
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

    private static int ParseContentLength(string headers)
    {
        string contentLengthHeader = headers
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(header => header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        return int.Parse(contentLengthHeader["Content-Length:".Length..].Trim());
    }
}
