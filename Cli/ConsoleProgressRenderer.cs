using System.Diagnostics;

namespace SqlDeps.Cli;

internal sealed class ConsoleProgressRenderer
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly object gate = new();
    private long lastRender;
    private string lastLine = "";
    private string lastPhase = "";

    public static ConsoleProgressRenderer? Create(bool asJson, bool quiet)
    {
        if (asJson || quiet || Console.IsOutputRedirected || Console.IsErrorRedirected || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
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
            if (!isComplete && progress.Phase == lastPhase && now - lastRender < Stopwatch.Frequency / 10)
            {
                return;
            }

            lastPhase = progress.Phase;
            lastRender = now;
            WriteLine(Render(progress));
        }
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
        var filled = completed * 24 / progress.Total;
        var bar = new string('#', filled) + new string('-', 24 - filled);
        var percent = completed * 100 / progress.Total;

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
