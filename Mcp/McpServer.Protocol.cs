using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace SqlDeps.Mcp;

internal static partial class McpServer
{
    private sealed partial class McpServerSession
    {
        internal void Run()
        {
            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Process(line);
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "The MCP request boundary must convert unexpected failures into JSON-RPC errors.")]
        private void Process(string line)
        {
            JsonElement? requestId = null;

            try
            {
                if (line.Length > MaxRequestCharacters)
                {
                    WriteError(requestId, JsonRpcInvalidRequestCode, $"MCP request exceeds the {MaxRequestCharacters / (1024 * 1024)} MiB limit.");

                    return;
                }

                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                requestId = GetRequestId(request);

                if (!IsValidRequest(request))
                {
                    WriteError(requestId, JsonRpcInvalidRequestCode, "Invalid Request.");

                    return;
                }

                var method = GetMethod(request);

                if (method is "notifications/initialized" or "notifications/cancelled")
                {
                    return;
                }

                if (requestId is null)
                {
                    return;
                }

                var result = Handle(method, GetParameters(request));
                WriteResponse(requestId.Value, result);
            }
            catch (JsonException exception)
            {
                WriteError(requestId, JsonRpcParseErrorCode, "Invalid JSON: " + exception.Message);
            }
            catch (MethodNotFoundException exception)
            {
                WriteError(requestId, JsonRpcMethodNotFoundCode, exception.Message);
            }
            catch (ArgumentException exception)
            {
                WriteError(requestId, JsonRpcInvalidParametersCode, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                WriteError(requestId, JsonRpcInternalErrorCode, exception.Message);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("sqldeps-mcp: request failed: " + exception.Message);
                WriteError(requestId, JsonRpcInternalErrorCode, "MCP internal error.");
            }
        }

        private object Handle(string method, JsonElement parameters)
        {
            return method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new { },
                "tools/list" => new { tools = McpToolCatalog.CreateDefinitions() },
                "resources/list" => ResourcesList(),
                "resources/read" => ReadResource(parameters),
                "tools/call" => CallTool(parameters),
                _ => throw new MethodNotFoundException("Unknown MCP method: " + method),
            };
        }

        private static JsonElement? GetRequestId(JsonElement request)
        {
            return request.ValueKind == JsonValueKind.Object
                && request.TryGetProperty("id", out var id)
                ? id.Clone()
                : null;
        }

        private static bool IsValidRequest(JsonElement request)
        {
            if (request.ValueKind != JsonValueKind.Object
                || !request.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), JsonRpcVersion, StringComparison.Ordinal)
                || !request.TryGetProperty("method", out var method)
                || method.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(method.GetString()))
            {
                return false;
            }

            return !request.TryGetProperty("id", out var id)
                || id.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null;
        }

        private static string GetMethod(JsonElement request)
        {
            if (!request.TryGetProperty("method", out var method))
            {
                throw new ArgumentException("MCP request method is required.");
            }

            if (method.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("MCP request method is required.");
            }

            if (string.IsNullOrWhiteSpace(method.GetString()))
            {
                throw new ArgumentException("MCP request method is required.");
            }

            return method.GetString()!;
        }

        private static JsonElement GetParameters(JsonElement request)
        {
            return request.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                ? parameters
                : default;
        }

        private static void WriteResponse(JsonElement requestId, object result)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                jsonrpc = JsonRpcVersion,
                id = requestId,
                result
            }, JsonOptions));
        }

        private static void WriteError(JsonElement? requestId, int code, string message)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    jsonrpc = JsonRpcVersion,
                    id = requestId,
                    error = new
                    {
                        code,
                        message
                    }
                },
                JsonOptions
            ));
        }

        private sealed class MethodNotFoundException(string message) : Exception(message);
    }
}
