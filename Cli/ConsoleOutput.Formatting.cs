using System.Text;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
    private static int OutputWidth()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out int columns) && columns > 1) return columns - 1;
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
}
