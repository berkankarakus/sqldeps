using System.Text.Json;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
    private const string SarifSchema = "https://json.schemastore.org/sarif-2.1.0.json";

    public static void WriteSarif(IReadOnlyList<Dictionary<string, object?>> rows, string profile = "default")
    {
        Dictionary<string, object?> log = new()
        {
            ["$schema"] = SarifSchema,
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "sqldeps",
                            informationUri = "https://github.com/berkankarakus/sqldeps",
                        },
                    },
                    properties = new { profile },
                    results = rows.Select(CreateSarifResult).ToArray(),
                },
            },
        };

        Console.WriteLine(JsonSerializer.Serialize(log, jsonSerializerOptions));
    }

    private static object CreateSarifResult(IReadOnlyDictionary<string, object?> row)
    {
        string rule = ReadString(row, "rule");
        string severity = ReadString(row, "severity");
        string sourceFile = ReadString(row, "source_file");
        int line = ReadInt(row, "line");
        Dictionary<string, object?> result = new()
        {
            ["ruleId"] = rule,
            ["level"] = SarifLevel(severity),
            ["message"] = new
            {
                text = ReadString(row, "message")
            },
            ["properties"] = new
            {
                source = ReadString(row, "source"),
                confidence = ReadString(row, "confidence"),
                stale = ReadBool(row, "stale"),
            },
        };

        if (!string.IsNullOrWhiteSpace(sourceFile) && line > 0)
        {
            result["locations"] = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = new Uri(Path.GetFullPath(sourceFile)).AbsoluteUri },
                        region = new { startLine = line },
                    },
                },
            };
        }

        return result;
    }

    private static string SarifLevel(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            _ => "note",
        };
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) ? value?.ToString() ?? "" : "";
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) && int.TryParse(value?.ToString(), out int result)
            ? result
            : 0;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) && bool.TryParse(value?.ToString(), out bool result) && result;
    }
}
