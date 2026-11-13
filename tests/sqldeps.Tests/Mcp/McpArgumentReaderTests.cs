using System.Text.Json;
using SqlDeps.Mcp;

namespace SqlDeps.Tests.Mcp;

public sealed class McpArgumentReaderTests
{
    [Fact]
    public void GetRequiredString_ReturnsNonEmptyString()
    {
        var arguments = Parse("{\"text\":\"SELECT 1;\"}");

        var value = McpArgumentReader.GetRequiredString(arguments, "text");

        Assert.Equal("SELECT 1;", value);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"text\":123}")]
    [InlineData("{\"text\":\"   \"}")]
    public void GetRequiredString_RejectsMissingOrInvalidValues(string json)
    {
        var arguments = Parse(json);

        var exception = Assert.Throws<ArgumentException>(
            () => McpArgumentReader.GetRequiredString(arguments, "text")
        );

        Assert.Contains("text", exception.Message);
    }

    [Fact]
    public void GetLimit_ReturnsNullWhenArgumentIsAbsentAndValueWhenPositive()
    {
        var missing = Parse("{}");
        var valid = Parse("{\"limit\":2}");

        Assert.Null(McpArgumentReader.GetLimit(missing));
        Assert.Equal(2, McpArgumentReader.GetLimit(valid));
    }

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":-1}")]
    [InlineData("{\"limit\":\"2\"}")]
    [InlineData("{\"limit\":1.5}")]
    public void GetLimit_RejectsNonPositiveOrNonIntegerValues(string json)
    {
        var arguments = Parse(json);

        var exception = Assert.Throws<ArgumentException>(
            () => McpArgumentReader.GetLimit(arguments)
        );

        Assert.Contains("positive integer", exception.Message);
    }

    [Fact]
    public void GetOptionalPositiveInteger_UsesTheRequestedPropertyName()
    {
        var arguments = Parse("{\"depth\":10}");

        Assert.Equal(10, McpArgumentReader.GetOptionalPositiveInteger(arguments, "depth"));
        Assert.Null(McpArgumentReader.GetOptionalPositiveInteger(Parse("{}"), "depth"));
    }

    [Fact]
    public void GetOptionalBoolean_ReturnsFalseWhenAbsentAndPreservesBooleanValue()
    {
        var missing = Parse("{}");
        var enabled = Parse("{\"join\":true}");
        var disabled = Parse("{\"join\":false}");

        Assert.False(McpArgumentReader.GetOptionalBoolean(missing, "join"));
        Assert.True(McpArgumentReader.GetOptionalBoolean(enabled, "join"));
        Assert.False(McpArgumentReader.GetOptionalBoolean(disabled, "join"));
    }

    [Fact]
    public void GetOptionalBoolean_RejectsNonBooleanValue()
    {
        var arguments = Parse("{\"join\":\"true\"}");

        var exception = Assert.Throws<ArgumentException>(
            () => McpArgumentReader.GetOptionalBoolean(arguments, "join")
        );

        Assert.Contains("boolean", exception.Message);
    }

    [Fact]
    public void GetOptionalString_ReturnsValueAndTreatsBlankAsMissing()
    {
        var value = Parse("{\"source_file\":\"live.sql\"}");
        var blank = Parse("{\"source_file\":\"  \"}");

        Assert.Equal("live.sql", McpArgumentReader.GetOptionalString(value, "source_file"));
        Assert.Null(McpArgumentReader.GetOptionalString(blank, "source_file"));
    }

    [Fact]
    public void GetOptionalString_RejectsNonStringValue()
    {
        var arguments = Parse("{\"source_file\":true}");

        var exception = Assert.Throws<ArgumentException>(
            () => McpArgumentReader.GetOptionalString(arguments, "source_file")
        );

        Assert.Contains("string", exception.Message);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
