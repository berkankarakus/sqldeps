using System.Text;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
    private static int OutputWidth()
    {
        if (TryReadConfiguredWidth(out var configuredWidth))
        {
            return configuredWidth;
        }

        try
        {
            var consoleWidth = Console.WindowWidth;

            return consoleWidth > 1 ? consoleWidth - 1 : 120;
        }

        catch (IOException)
        {
            return 120;
        }
    }

    private static bool TryReadConfiguredWidth(out int width)
    {
        width = 0;

        if (!int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns))
        {
            return false;
        }

        if (columns <= 1)
        {
            return false;
        }

        width = columns - 1;

        return true;
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
}
