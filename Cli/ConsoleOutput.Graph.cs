using System.Text;
using System.Text.Json;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
    public static void WriteGraph(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string format)
    {
        if (string.Equals(format, "dot", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ToDot(rows));

            return;
        }

        var nodes = rows
            .SelectMany(row => new[] { ReadGraphValue(row, "source_object"), ReadGraphValue(row, "target_object") })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new { id = name })
            .ToArray();
        var edges = rows.Select(row => new
        {
            source = ReadGraphValue(row, "source_object"),
            target = ReadGraphValue(row, "target_object"),
            operation = ReadGraphValue(row, "operation"),
            source_file = ReadGraphValue(row, "source_file"),
            line = ReadGraphInt(row, "line"),
            via_dynamic = ReadGraphBool(row, "via_dynamic"),
        }).ToArray();

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                format = "sqldeps.graph.v1",
                nodes,
                edges
            },
            jsonSerializerOptions));
    }

    private static string ToDot(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        StringBuilder output = new();
        output.AppendLine("digraph sqldeps {");
        output.AppendLine("  rankdir=LR;");

        foreach (var row in rows)
        {
            string source = ReadGraphValue(row, "source_object");
            string target = ReadGraphValue(row, "target_object");
            string operation = ReadGraphValue(row, "operation");
            string dynamicSuffix = ReadGraphBool(row, "via_dynamic") ? " (dynamic)" : "";
            output.AppendLine($"  \"{EscapeDot(source)}\" -> \"{EscapeDot(target)}\" [label=\"{EscapeDot(operation + dynamicSuffix)}\"];");
        }

        output.AppendLine("}");

        return output.ToString().TrimEnd();
    }

    private static string EscapeDot(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string ReadGraphValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) ? value?.ToString() ?? "" : "";
    }

    private static int ReadGraphInt(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) && int.TryParse(value?.ToString(), out int result)
            ? result
            : 0;
    }

    private static bool ReadGraphBool(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value)
            && (value is true || value?.ToString() is "1" or "true");
    }
}
