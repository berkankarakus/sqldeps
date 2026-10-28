using System.Text.Json;

namespace SqlDeps;

/// <summary>Writes query results as either a readable table or stable JSON.</summary>
public static class ConsoleOutput
{
    public static void WriteRows(IReadOnlyList<Dictionary<string, object?>> rows, bool asJson, bool useColor)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (rows.Count == 0)
        {
            Console.WriteLine("No results.");
            return;
        }

        var columns = rows[0].Keys.ToArray();
        var textRows = rows.Select(row => columns.Select(column => row[column]?.ToString() ?? "").ToArray()).ToArray();
        var widths = columns.Select((column, index) => Math.Max(column.Length, textRows.Max(row => row[index].Length))).ToArray();
        WriteLine(columns, widths, useColor ? ConsoleColor.Cyan : null);
        WriteLine(widths.Select(width => new string('-', width)).ToArray(), widths, null);

        foreach (var row in textRows)
        {
            WriteLine(row, widths, null);
        }
    }

    public static void WriteStatus(IndexStatus status, bool asJson, bool useColor)
    {
        WriteRows(new[]
        {
            new Dictionary<string, object?>
            {
                ["sql_files"] = status.SqlFiles, ["indexed_files"] = status.IndexedFiles,
                ["added"] = status.Added, ["changed"] = status.Changed, ["removed"] = status.Removed,
                ["objects"] = status.Objects, ["dependencies"] = status.Dependencies, ["parse_errors"] = status.ParseErrors,
            },
        }, asJson, useColor);
        if (!asJson)
        {
            Console.WriteLine(status.IsCurrent ? "Index is current." : "Index is stale; run sqldeps index again.");
        }
    }

    private static void WriteLine(IEnumerable<string> values, IReadOnlyList<int> widths, ConsoleColor? color)
    {
        var previous = Console.ForegroundColor;

        if (color.HasValue && !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null)
        {
            Console.ForegroundColor = color.Value;
        }

        Console.WriteLine(string.Join("  ", values.Select((value, index) => value.PadRight(widths[index]))));
        Console.ForegroundColor = previous;
    }
}
