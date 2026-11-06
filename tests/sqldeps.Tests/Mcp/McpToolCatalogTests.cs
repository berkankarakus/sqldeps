using System.Text.Json;
using SqlDeps.Mcp;

namespace SqlDeps.Tests.Mcp;

public sealed class McpToolCatalogTests
{
    [Fact]
    public void CreateDefinitions_ContainsReadOnlyAnalysisSurfaces()
    {
        var definitions = McpToolCatalog.CreateDefinitions();
        var names = definitions
            .Select(definition => JsonSerializer.SerializeToElement(definition).GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("analyze", names);
        Assert.Contains("code_actions", names);
        Assert.Contains("query_builder", names);
        Assert.Contains("drift", names);
        Assert.Contains("refactor_candidates", names);
        Assert.Contains("parameterize_dynamic_sql", names);
        Assert.Contains("test_skeleton", names);
        Assert.DoesNotContain("execute", names);
        Assert.DoesNotContain("write_file", names);
    }

    [Fact]
    public void CreateDefinitions_DeclaresRequiredArgumentsConsistently()
    {
        var definitions = McpToolCatalog.CreateDefinitions();

        foreach (var definition in definitions)
        {
            var document = JsonSerializer.SerializeToElement(definition);
            var schema = document.GetProperty("inputSchema");
            var properties = schema.GetProperty("properties");
            var required = schema.GetProperty("required").EnumerateArray();

            foreach (var property in required)
            {
                Assert.True(properties.TryGetProperty(property.GetString()!, out _));
            }
        }
    }

    [Fact]
    public void CreateDefinitions_ProvidesDiscoverableObjectSchemas()
    {
        var definitions = McpToolCatalog.CreateDefinitions();

        foreach (var definition in definitions)
        {
            var document = JsonSerializer.SerializeToElement(definition);

            Assert.False(string.IsNullOrWhiteSpace(document.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(document.GetProperty("description").GetString()));
            Assert.Equal(
                "object",
                document.GetProperty("inputSchema").GetProperty("type").GetString());
        }
    }

    [Fact]
    public void CreateDefinitions_UsesPositiveMinimumForIntegerArguments()
    {
        var definitions = McpToolCatalog.CreateDefinitions();
        var limitProperties = definitions
            .Select(definition => JsonSerializer.SerializeToElement(definition).GetProperty("inputSchema").GetProperty("properties"))
            .Where(properties => properties.TryGetProperty("limit", out _))
            .ToArray();

        Assert.NotEmpty(limitProperties);

        foreach (var properties in limitProperties)
        {
            var limit = properties.GetProperty("limit");

            Assert.Equal("integer", limit.GetProperty("type").GetString());
            Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        }
    }
}
