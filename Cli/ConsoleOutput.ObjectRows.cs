using System.Text;

namespace SqlDeps.Cli;

public static partial class ConsoleOutput
{
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
            foreach (var row in group) AppendReference(output, Value(row, "source_object"), row);
        }
    }

    private static void AppendReaders(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        AppendColoredLine(output, "  READ", "\u001b[33m");
        foreach (var row in rows) AppendReference(output, Value(row, "source_object"), row);
    }

    private static void AppendDependencies(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var group in rows.GroupBy(row => string.Join("|", Value(row, "operation"), Value(row, "target_object"))))
        {
            var first = group.First();
            AppendOperation(output, Value(first, "operation"), Value(first, "target_object"));
            foreach (var row in group) AppendReference(output, "", row);
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
        if (location.Length > 0) output.Append("\u001b[2m").Append(subject.Length > 0 ? "  " : "").Append(location).Append("\u001b[0m");
        output.Append('\n');
    }

    private static string LocationOf(IReadOnlyDictionary<string, object?> row) =>
        row.TryGetValue("source_file", out var sourceFile) && row.TryGetValue("line", out var line) ? sourceFile + ":" + line : "";
}
