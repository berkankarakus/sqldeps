using System.Text;

namespace SqlDeps.Cli;

internal static partial class ConsoleOutput
{
    private static int TableWidth(IReadOnlyList<int> widths) => widths.Sum() + 2 * (widths.Count - 1);

    private static void AppendStackedRows(StringBuilder output, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, bool useColor)
    {
        foreach (var row in rows)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                AppendField(output, columns[index], row[index], useColor);
            }

            output.Append('\n');
        }
    }

    private static void AppendField(StringBuilder output, string name, string value, bool useColor)
    {
        const string indent = "  ";
        string prefix = indent + name + ": ";
        int available = Math.Max(1, OutputWidth() - prefix.Length);
        IEnumerable<string> lines = Wrap(value, available);

        if (useColor)
        {
            output.Append("\u001b[36m").Append(prefix).Append("\u001b[0m");
        }
        else
        {
            output.Append(prefix);
        }

        using var enumerator = lines.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            output.AppendLine();

            return;
        }

        output.AppendLine(enumerator.Current);

        while (enumerator.MoveNext())
        {
            output.Append(' ', prefix.Length).AppendLine(enumerator.Current);
        }
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (value.Length == 0)
        {
            yield return "";
            yield break;
        }

        for (var start = 0; start < value.Length; start += width)
        {
            yield return value.Substring(start, Math.Min(width, value.Length - start));
        }
    }

    private static void AppendLine(StringBuilder output, IEnumerable<string> values, IReadOnlyList<int> widths, string? color)
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

    private static void AppendDataLine(StringBuilder output, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<int> widths, bool useColor)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                output.Append("  ");
            }

            string? color = useColor ? ValueColor(columns[index]) : null;

            if (color is not null)
            {
                output.Append(color);
            }

            output.Append(values[index].PadRight(widths[index]));

            if (color is not null)
            {
                output.Append("\u001b[0m");
            }
        }

        output.Append('\n');
    }

    private static string? ValueColor(string column) => column switch
    {
        "name" or "source_object" or "target_object" or "affected" => "\u001b[36m",
        "kind" or "operation" => "\u001b[33m",
        "source_file" or "places" => "\u001b[2m",
        "message" => "\u001b[31m",
        _ => null,
    };
}
