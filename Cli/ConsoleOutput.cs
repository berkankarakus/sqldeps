using System.Text;
using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Cli;

/// <summary>Writes query results as either a readable table or stable JSON.</summary>
public static partial class ConsoleOutput
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

    public static void WriteObjectRows(string command, string objectName, IReadOnlyList<Dictionary<string, object?>> rows, bool asJson, bool useColor, bool usePager)
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

    public static void WriteRows(IReadOnlyList<Dictionary<string, object?>> rows, bool asJson, bool useColor, bool usePager)
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
        if (TableWidth(widths) > OutputWidth())
        {
            AppendStackedRows(output, columns, textRows, useColor);
            WriteOutput(output.ToString(), useColor, usePager);
            return;
        }

        AppendLine(output, columns, widths, useColor ? "\u001b[36m" : null);
        AppendLine(output, [.. widths.Select(width => new string('-', width))], widths, useColor ? "\u001b[2m" : null);
        foreach (var row in textRows) AppendDataLine(output, columns, row, widths, useColor);
        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteQueryPreview(QueryPreview preview, bool asJson, bool useColor, bool usePager)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                object_name = preview.ObjectName,
                object_kind = preview.ObjectKind,
                columns = preview.Columns,
                sql = preview.Sql,
            }, jsonSerializerOptions));
            return;
        }

        var output = new StringBuilder();
        AppendColoredLine(output, $"Query preview: {preview.ObjectName} ({preview.ObjectKind})", "\u001b[36m");
        output.AppendLine(preview.Sql);
        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteTestSkeleton(SqlTestSkeleton skeleton, bool asJson, bool useColor, bool usePager)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                object_name = skeleton.ObjectName,
                parameters = skeleton.Parameters,
                sql = skeleton.Sql,
            }, jsonSerializerOptions));

            return;
        }

        var output = new StringBuilder();
        AppendColoredLine(output, $"Test skeleton: {skeleton.ObjectName}", "\u001b[36m");
        output.AppendLine(skeleton.Sql);
        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteDiagnosticExplanation(DiagnosticExplanation explanation, bool asJson, bool useColor, bool usePager)
    {

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                rule = explanation.Rule,
                summary = explanation.Summary,
                guidance = explanation.Guidance,
                default_severity = explanation.DefaultSeverity,
            }, jsonSerializerOptions));


            return;
        }

        var output = new StringBuilder();
        AppendColoredLine(output, $"Diagnostic: {explanation.Rule}", "\u001b[36m");
        output.AppendLine($"Severity: {explanation.DefaultSeverity}");
        output.AppendLine($"Why: {explanation.Summary}");
        output.AppendLine($"Suggested direction: {explanation.Guidance}");
        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteStatus(IndexStatus status, bool asJson, bool useColor)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                sql_files = status.SqlFiles,
                indexed_files = status.IndexedFiles,
                added = status.Added,
                changed = status.Changed,
                removed = status.Removed,
                objects = status.Objects,
                dependencies = status.Dependencies,
                parse_errors = status.ParseErrors,
                is_current = status.IsCurrent,
            }, jsonSerializerOptions));
            return;
        }

        WriteRows(
            [new Dictionary<string, object?>
            {
                ["sql_files"] = status.SqlFiles, ["indexed_files"] = status.IndexedFiles,
                ["added"] = status.Added, ["changed"] = status.Changed, ["removed"] = status.Removed,
                ["objects"] = status.Objects, ["dependencies"] = status.Dependencies, ["parse_errors"] = status.ParseErrors,
            }],
            asJson: false,
            useColor,
            usePager: false
        );
        WriteOutput(
            status.IsCurrent ? "\u001b[32mIndex is current.\u001b[0m\n" : "\u001b[31mIndex is stale; run sqldeps index again.\u001b[0m\n",
            useColor,
            usePager: false
        );
    }

    public static void WriteReport(
        IndexStatus status,
        IReadOnlyList<IndexDrift> drift,
        IReadOnlyDictionary<string, int> lintCounts,
        bool asJson,
        bool useColor,
        bool usePager)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                is_current = status.IsCurrent,
                status = new
                {
                    sql_files = status.SqlFiles,
                    indexed_files = status.IndexedFiles,
                    added = status.Added,
                    changed = status.Changed,
                    removed = status.Removed,
                    objects = status.Objects,
                    dependencies = status.Dependencies,
                    parse_errors = status.ParseErrors,
                },
                drift = drift.Select(item => new { state = item.State, path = item.Path }),
                lint = new
                {
                    error = lintCounts.GetValueOrDefault("error"),
                    warning = lintCounts.GetValueOrDefault("warning"),
                    info = lintCounts.GetValueOrDefault("info"),
                },
            }, jsonSerializerOptions));

            return;
        }

        var output = new StringBuilder();
        AppendColoredLine(output, "Index report", "\u001b[36m");
        output.AppendLine($"SQL files: {status.SqlFiles} (indexed {status.IndexedFiles})");
        output.AppendLine($"Objects: {status.Objects}, dependencies: {status.Dependencies}, parse errors: {status.ParseErrors}");
        output.AppendLine(status.IsCurrent ? "Drift: none" : $"Drift: {drift.Count} file(s)");
        output.AppendLine($"Lint: {lintCounts.GetValueOrDefault("error")} error, {lintCounts.GetValueOrDefault("warning")} warning, {lintCounts.GetValueOrDefault("info")} info");

        foreach (var item in drift)
        {
            output.AppendLine($"  {item.State}: {item.Path}");
        }

        WriteOutput(output.ToString(), useColor, usePager);
    }

    public static void WriteIndexSummary(int sqlFiles, int reusedFiles, int objects, int dependencies, int parseErrors, int warnings, int reachPairs, int cycles, string databasePath)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sql_files = sqlFiles,
            reused_files = reusedFiles,
            objects,
            dependencies,
            parse_errors = parseErrors,
            warnings,
            reach_pairs = reachPairs,
            cycles,
            database = databasePath,
        }, jsonSerializerOptions));
    }

    public static void WriteActiveIndex(string? databasePath, bool active)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            active,
            name = databasePath is null ? null : Path.GetFileNameWithoutExtension(databasePath),
            database = databasePath,
        }, jsonSerializerOptions));
    }

    public static void WriteDropResult(string name, string databasePath, bool deleted, bool cancelled)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { name, database = databasePath, deleted, cancelled }, jsonSerializerOptions));
    }
}
