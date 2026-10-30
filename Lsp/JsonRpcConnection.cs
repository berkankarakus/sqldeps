using System.Text;
using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Minimal LSP-compatible JSON-RPC transport over standard input and output.</summary>
internal sealed class JsonRpcConnection(Stream input, Stream output)
{
    private const int MaxHeaderBytes = 8192;
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    public JsonDocument? Read()
    {
        var contentLength = -1;

        while (true)
        {
            string? line = ReadHeaderLine();
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (TryReadContentLength(line, out var length))
            {
                if (length < 0 || length > MaxMessageBytes)
                {
                    throw new InvalidDataException($"LSP request body exceeds the {MaxMessageBytes} byte limit.");
                }

                contentLength = length;
            }
        }

        if (contentLength < 0)
        {
            throw new InvalidDataException("LSP request has no Content-Length header.");
        }

        byte[] bytes = new byte[contentLength];
        var read = 0;
        while (read < bytes.Length)
        {
            int count = input.Read(bytes, read, bytes.Length - read);
            if (count == 0)
            {
                throw new EndOfStreamException("LSP request ended before its JSON body.");
            }

            read += count;
        }

        return JsonDocument.Parse(bytes);
    }

    private static bool TryReadContentLength(string line, out int length)
    {
        const string header = "Content-Length:";

        if (!line.StartsWith(header, StringComparison.OrdinalIgnoreCase))
        {
            length = 0;

            return false;
        }

        return int.TryParse(line[header.Length..].Trim(), out length);
    }

    public void Respond(JsonElement id, object? result)
    {
        WriteResponse(id, "result", result);
    }

    public void RespondError(JsonElement id, int code, string message)
    {
        WriteResponse(id, "error", new { code, message });
    }

    private void WriteResponse(JsonElement id, string propertyName, object? value)
    {
        using MemoryStream payload = new();
        using (Utf8JsonWriter writer = new(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName(propertyName);
            JsonSerializer.Serialize(writer, value);
            writer.WriteEndObject();
        }

        Write(payload.ToArray());
    }

    public void Notify(string method, object parameters)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", method, @params = parameters });
        Write(payload);
    }

    private void Write(byte[] payload)
    {
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        output.Write(header);
        output.Write(payload);
        output.Flush();
    }

    private string? ReadHeaderLine()
    {
        List<byte> bytes = [];

        while (true)
        {
            int value = input.ReadByte();
            if (value < 0)
            {
                return bytes.Count == 0 ? null : throw new EndOfStreamException("LSP header ended unexpectedly.");
            }

            if (value == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString([.. bytes]);
            }

            bytes.Add((byte)value);

            if (bytes.Count > MaxHeaderBytes)
            {
                throw new InvalidDataException($"LSP header exceeds the {MaxHeaderBytes} byte limit.");
            }
        }
    }
}
