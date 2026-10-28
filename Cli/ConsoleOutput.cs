using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SqlDeps.Cli;

/// <summary>Writes query results as either a readable table or stable JSON.</summary>
public static class ConsoleOutput
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

    public static void WriteObjectRows(
        string command,
        string objectName,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool asJson,
        bool useColor,
        bool usePager
    )
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, jsonSerializerOptions));

            return;
        }

        if (rows.Count == 0)
        {
            WriteOutput("No results.\n", useColor, usePager: false);

            return;
        }

        StringBuilder output = new();
        AppendColoredLine(output, objectName, "\u001b[36m");

        switch (command)
        {
            case "columns":
                AppendColumns(output, rows);
                break;
            case "who-writes":
            case "who-updates":
                AppendWriters(output, rows);
                break;
            case "who-reads":
                AppendReaders(output, rows);
                break;
            case "deps":
                AppendDependencies(output, rows);
                break;
            case "impact":
                AppendImpact(output, rows);
                break;
            default:
                throw new ArgumentException("Unsupported object query: " + command);
        }

        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool asJson,
        bool useColor,
        bool usePager
    )
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, jsonSerializerOptions));

            return;
        }

        if (rows.Count == 0)
        {
            WriteOutput("No results.\n", useColor, usePager: false);

            return;
        }

        var columns = rows[0].Keys.ToArray();
        var textRows = rows.Select(row => columns.Select(column => row[column]?.ToString() ?? "").ToArray()).ToArray();
        var widths = columns.Select((column, index) => Math.Max(column.Length, textRows.Max(row => row[index].Length))).ToArray();
        StringBuilder output = new();
        AppendLine(output, columns, widths, useColor ? "\u001b[36m" : null);
        AppendLine(output, [.. widths.Select(width => new string('-', width))], widths, useColor ? "\u001b[2m" : null);

        foreach (var row in textRows)
        {
            AppendLine(output, row, widths, null);
        }

        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteStatus(IndexStatus status, bool asJson, bool useColor)
    {
        WriteRows(
            [
                new Dictionary<string, object?>
                {
                    ["sql_files"] = status.SqlFiles, ["indexed_files"] = status.IndexedFiles,
                    ["added"] = status.Added, ["changed"] = status.Changed, ["removed"] = status.Removed,
                    ["objects"] = status.Objects, ["dependencies"] = status.Dependencies, ["parse_errors"] = status.ParseErrors,
                },
            ],
            asJson,
            useColor,
            usePager: false
        );

        if (!asJson)
        {
            Console.WriteLine(status.IsCurrent ? "Index is current." : "Index is stale; run sqldeps index again.");
        }
    }

    private static void AppendColumns(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var group in rows.GroupBy(row => string.Join("|", Value(row, "operation"), Value(row, "table_name"), Value(row, "source_file"), Value(row, "line"))))
        {
            var first = group.First();
            AppendOperation(output, Value(first, "operation"), Value(first, "table_name"));
            AppendReference(output, string.Join(", ", group.Select(row => Value(row, "column_name"))), first);
        }
    }

    private static void AppendWriters(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var group in rows.GroupBy(row => Value(row, "operation")))
        {
            AppendColoredLine(output, "  " + group.Key, "\u001b[33m");

            foreach (var row in group)
            {
                AppendReference(output, Value(row, "source_object"), row);
            }
        }
    }

    private static void AppendReaders(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        AppendColoredLine(output, "  READ", "\u001b[33m");

        foreach (var row in rows)
        {
            AppendReference(output, Value(row, "source_object"), row);
        }
    }

    private static void AppendDependencies(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var group in rows.GroupBy(row => string.Join("|", Value(row, "operation"), Value(row, "target_object"))))
        {
            var first = group.First();
            AppendOperation(output, Value(first, "operation"), Value(first, "target_object"));

            foreach (var row in group)
            {
                AppendReference(output, "", row);
            }
        }
    }

    private static void AppendImpact(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            output.Append("  • ").Append(Value(row, "affected")).Append(" (depth ").Append(Value(row, "depth")).AppendLine(")");
            output.Append("    ").AppendLine(Value(row, "path"));
        }
    }

    private static void AppendOperation(StringBuilder output, string operation, string target)
    {
        output.Append("  \u001b[33m").Append(operation).Append("\u001b[0m  ").AppendLine(target);
    }

    private static void AppendReference(StringBuilder output, string subject, IReadOnlyDictionary<string, object?> row)
    {
        string location = LocationOf(row);
        string prefix = "    • ";
        string text = prefix + subject + (subject.Length > 0 && location.Length > 0 ? "  " : "") + location;

        if (text.Length > OutputWidth())
        {
            output.AppendLine(Truncate(text));

            return;
        }

        output.Append(prefix).Append(subject);

        if (location.Length > 0)
        {
            output.Append("\u001b[2m").Append(subject.Length > 0 ? "  " : "").Append(location).Append("\u001b[0m");
        }

        output.Append('\n');
    }

    private static string LocationOf(IReadOnlyDictionary<string, object?> row) =>
        row.TryGetValue("source_file", out var sourceFile) && row.TryGetValue("line", out var line)
            ? sourceFile + ":" + line
            : "";

    private static int OutputWidth()
    {
        try
        {
            return Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 120;
        }
        catch (IOException)
        {
            return 120;
        }
    }

    private static string Truncate(string text)
    {
        const string ellipsis = "…";
        int width = Math.Max(OutputWidth(), ellipsis.Length + 1);
        return text.Length <= width ? text : text[..(width - ellipsis.Length)] + ellipsis;
    }

    private static void AppendColoredLine(StringBuilder output, string text, string color)
    {
        output.Append(color).Append(text).Append("\u001b[0m\n");
    }

    private static string Value(IReadOnlyDictionary<string, object?> row, string key) => row[key]?.ToString() ?? "";

    private static void AppendLine(
        StringBuilder output,
        IEnumerable<string> values,
        IReadOnlyList<int> widths,
        string? color
    )
    {
        if (color is not null)
        {
            output.Append(color);
        }

        output.AppendLine(string.Join("  ", values.Select((value, index) => value.PadRight(widths[index]))));

        if (color is not null)
        {
            output.Append("\u001b[0m");
        }
    }

    private static void WriteOutput(string output, bool useColor, bool usePager)
    {
        if (!useColor || Environment.GetEnvironmentVariable("NO_COLOR") is not null || Console.IsOutputRedirected)
        {
            output = output.Replace("\u001b[36m", "").Replace("\u001b[33m", "").Replace("\u001b[2m", "").Replace("\u001b[0m", "");
        }

        if (!usePager || Console.IsOutputRedirected || !ExceedsTerminalHeight(output))
        {
            Console.Write(output);

            return;
        }

        try
        {
            ProcessStartInfo startInfo = new("less");
            startInfo.ArgumentList.Add("-R");
            startInfo.RedirectStandardInput = true;
            startInfo.UseShellExecute = false;

            using Process? pager = Process.Start(startInfo);

            if (pager is null)
            {
                Console.Write(output);

                return;
            }

            pager.StandardInput.Write(output);
            pager.StandardInput.Close();
            pager.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Write(output);
        }
    }

    private static bool ExceedsTerminalHeight(string output)
    {
        try
        {
            return Console.WindowHeight > 1 && output.Count(character => character == '\n') >= Console.WindowHeight - 1;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
