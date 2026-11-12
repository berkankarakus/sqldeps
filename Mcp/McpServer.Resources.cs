using System.Reflection;
using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Mcp;

internal static partial class McpServer
{
    private sealed partial class McpServerSession
    {
        private static object Initialize(JsonElement parameters)
        {
            string? requestedVersion = parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("protocolVersion", out var requestedVersionProperty)
                && requestedVersionProperty.ValueKind == JsonValueKind.String
                ? requestedVersionProperty.GetString()
                : null;

            if (requestedVersion is not null
                && !string.Equals(requestedVersion, ProtocolVersion, StringComparison.Ordinal))
            {
                throw new ArgumentException("Unsupported MCP protocol version: " + requestedVersion);
            }

            return new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    },
                    resources = new
                    {
                        subscribe = false,
                        listChanged = false
                    },
                },
                serverInfo = new
                {
                    name = "sqldeps",
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? UnknownVersion,
                },
            };
        }

        private static object ResourcesList()
        {
            var resources = DiagnosticRuleCatalog.All.Select(explanation => new
            {
                uri = DiagnosticResourceUri(explanation.Rule),
                name = explanation.Rule,
                description = explanation.Summary,
                mimeType = "application/json",
            });

            return new
            {
                resources = resources.ToArray()
            };
        }

        private static object ReadResource(JsonElement parameters)
        {
            var uri = McpArgumentReader.GetRequiredString(parameters, "uri");
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var resourceUri)
                || !string.Equals(resourceUri.Scheme, "sqldeps", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(resourceUri.Host, "diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("MCP resource URI must use sqldeps://diagnostic/{rule}.");
            }

            var rule = resourceUri.AbsolutePath.Trim('/');
            if (rule.Length == 0 || !DiagnosticRuleCatalog.TryGet(rule, out var explanation))
            {
                throw new ArgumentException("Unknown diagnostic resource: " + rule);
            }

            var content = new
            {
                rule = explanation.Rule,
                summary = explanation.Summary,
                guidance = explanation.Guidance,
                default_severity = explanation.DefaultSeverity,
                references = explanation.References ?? [],
            };

            return new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "application/json",
                        text = JsonSerializer.Serialize(content, JsonOptions),
                    },
                },
            };
        }

        private static string DiagnosticResourceUri(string rule) => "sqldeps://diagnostic/" + rule;
    }
}
