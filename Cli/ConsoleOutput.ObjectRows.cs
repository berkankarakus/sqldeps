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
            AppendGroupedReferences(output, group, "source_object");
        }
    }

    private static void AppendReaders(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        AppendColoredLine(output, "  READ", "\u001b[33m");
        AppendGroupedReferences(output, rows, "source_object");
    }

    private static void AppendGroupedReferences(
        StringBuilder output,
        IEnumerable<Dictionary<string, object?>> rows,
        string subjectColumn)
    {
        foreach (var group in rows.GroupBy(row => (Subject: subjectColumn.Length == 0 ? "" : Value(row, subjectColumn), File: Value(row, "source_file"))))
        {
            var lines = group
                .Select(row => Value(row, "line"))
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var location = new Dictionary<string, object?>
            {
                ["source_file"] = group.Key.File,
                ["line"] = string.Join(", ", lines),
            };
            AppendReference(output, group.Key.Subject, location);
        }
    }

    private static void AppendDependencies(StringBuilder output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var group in rows.GroupBy(row => string.Join("|", Value(row, "operation"), Value(row, "target_object"))))
        {
            var first = group.First();
            AppendOperation(output, Value(first, "operation"), Value(first, "target_object"));
            AppendGroupedReferences(output, group, "");
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
        string separator = ReferenceSeparator(subject, location);
        string text = prefix + subject + separator + location;

        if (text.Length > OutputWidth())
        {
            output.AppendLine(Truncate(text));

            return;
        }

        output.Append(prefix).Append(subject);
        if (location.Length > 0)
        {
            output.Append("\u001b[2m").Append(separator).Append(location).Append("\u001b[0m");
        }

        output.Append('\n');
    }

    private static string ReferenceSeparator(string subject, string location)
    {
        return subject.Length > 0 && location.Length > 0 ? "  " : "";
    }

    private static string LocationOf(IReadOnlyDictionary<string, object?> row) =>
        row.TryGetValue("source_file", out var sourceFile) && row.TryGetValue("line", out var line) ? sourceFile + ":" + line : "";
}
