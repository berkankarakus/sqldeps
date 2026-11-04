using System.Diagnostics;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
    private static void WriteOutput(string output, bool useColor, bool usePager)
    {

        if (ShouldStripColors(useColor))
        {
            output = StripColors(output);
        }

        if (!ShouldUsePager(usePager, output))
        {
            WriteToConsole(output);

            return;
        }

        WriteToPager(output);
    }

    private static bool ShouldStripColors(bool useColor) => !useColor
        || Environment.GetEnvironmentVariable("NO_COLOR") is not null
        || Console.IsOutputRedirected;

    private static bool ShouldUsePager(bool usePager, string output) => usePager
        && !Console.IsOutputRedirected
        && ExceedsTerminalHeight(output);

    private static void WriteToPager(string output)
    {
        try
        {
            ProcessStartInfo startInfo = new("less");
            startInfo.ArgumentList.Add("-R");
            startInfo.RedirectStandardInput = true;
            startInfo.UseShellExecute = false;
            using Process? pager = Process.Start(startInfo);

            if (pager is null)
            {
                WriteToConsole(output);

                return;
            }

            pager.StandardInput.Write(output);
            pager.StandardInput.Close();
            pager.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            WriteToConsole(output);
        }
        catch (IOException)
        {
            // A pager can quit before consuming every byte (for example, after the user presses q).
        }
    }

    private static void WriteToConsole(string output)
    {
        try
        {
            Console.Write(output);
        }
        catch (IOException)
        {
            // A consumer such as head may close stdout before every row is written.
        }
    }

    private static string StripColors(string output) => output
        .Replace("\u001b[31m", "")
        .Replace("\u001b[32m", "")
        .Replace("\u001b[36m", "")
        .Replace("\u001b[33m", "")
        .Replace("\u001b[2m", "")
        .Replace("\u001b[0m", "");

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
