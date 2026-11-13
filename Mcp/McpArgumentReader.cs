using System.Text.Json;

namespace SqlDeps.Mcp;

internal static class McpArgumentReader
{
    internal static int? GetLimit(JsonElement arguments)
    {
        return GetOptionalPositiveInteger(arguments, "limit");
    }

    internal static int? GetOptionalPositiveInteger(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value < 1)
        {
            throw new ArgumentException("MCP argument '" + propertyName + "' must be a positive integer.");
        }

        return value;
    }

    internal static bool GetOptionalBoolean(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("MCP argument '" + propertyName + "' must be a boolean.");
        }

        return property.GetBoolean();
    }

    internal static string GetRequiredString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw RequiredStringException(propertyName);
        }

        if (!value.TryGetProperty(propertyName, out var property))
        {
            throw RequiredStringException(propertyName);
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw RequiredStringException(propertyName);
        }

        if (string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw RequiredStringException(propertyName);
        }

        return property.GetString()!;
    }

    internal static string? GetOptionalString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("MCP argument '" + propertyName + "' must be a string.");
        }

        string? text = property.GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text;
    }

    private static ArgumentException RequiredStringException(string propertyName)
    {
        return new ArgumentException("MCP argument '" + propertyName + "' is required.");
    }
}
