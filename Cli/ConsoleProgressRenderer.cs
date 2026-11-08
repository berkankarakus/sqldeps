using System.Diagnostics;
using SqlDeps.Indexing;

namespace SqlDeps.Cli;

internal sealed class ConsoleProgressRenderer
{
    private const int ProgressBarWidth = 24;
    private const int PercentScale = 100;
    private static readonly long MinimumRenderTicks = Stopwatch.Frequency / 10;

    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly object gate = new();
    private long lastRender;
    private string lastLine = "";
    private string lastPhase = "";

    public static ConsoleProgressRenderer? Create(bool asJson, bool quiet)
    {
        if (!IsInteractiveOutput(asJson, quiet))
        {
            return null;
        }

        return new ConsoleProgressRenderer();
    }

    public void Report(IndexProgress progress)
    {
        lock (gate)
        {
            var now = Stopwatch.GetTimestamp();
            var isComplete = progress.Total > 0 && progress.Completed >= progress.Total;

            if (ShouldSkipRender(progress, isComplete, now))
            {
                return;
            }

            lastPhase = progress.Phase;
            lastRender = now;
            WriteLine(Render(progress));
        }
    }

    private static bool IsInteractiveOutput(bool asJson, bool quiet)
    {
        if (asJson || quiet)
        {
            return false;
        }

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return false;
        }

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
    }

    private bool ShouldSkipRender(IndexProgress progress, bool isComplete, long now)
    {
        if (isComplete)
        {
            return false;
        }

        if (progress.Phase != lastPhase)
        {
            return false;
        }

        return now - lastRender < MinimumRenderTicks;
    }

    public void Complete()
    {
        lock (gate)
        {
            if (lastLine.Length == 0)
            {
                return;
            }

            Console.Write('\r');
            Console.Write(new string(' ', lastLine.Length));
            Console.Write('\r');
            Console.WriteLine();
            lastLine = "";
        }
    }

    private string Render(IndexProgress progress)
    {
        var elapsed = stopwatch.Elapsed.TotalSeconds;
        if (progress.Total <= 0)
        {
            return $"{progress.Phase}... {elapsed:F1}s";
        }

        var completed = Math.Clamp(progress.Completed, 0, progress.Total);
        var filled = completed * ProgressBarWidth / progress.Total;
        var bar = new string('#', filled) + new string('-', ProgressBarWidth - filled);
        var percent = completed * PercentScale / progress.Total;

        return $"{progress.Phase,-13} [{bar}] {percent,3}% {completed}/{progress.Total} ({progress.ReusedFiles} reused) {elapsed:F1}s";
    }

    private void WriteLine(string line)
    {
        var padding = Math.Max(0, lastLine.Length - line.Length);
        Console.Write('\r');
        Console.Write(line);
        Console.Write(new string(' ', padding));
        lastLine = line;
    }
}
