using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private const int MaxDependencyRows = 100;

    private string ExecuteWhoWritesCommand(JsonElement parameters)
    {
        string objectName = RequiredCommandObjectName(parameters, "sqldeps.who-writes");
        ValidateIndexedObject(objectName);

        return FormatDependencyRows(
            $"Who writes: {objectName}",
            AllDependencies()
                .Where(dependency => SameName(dependency.TargetObject, objectName))
                .Where(dependency => IsWriteOperation(dependency.Operation)));
    }

    private string ExecuteWhoReadsCommand(JsonElement parameters)
    {
        string objectName = RequiredCommandObjectName(parameters, "sqldeps.who-reads");
        ValidateIndexedObject(objectName);

        return FormatDependencyRows(
            $"Who reads: {objectName}",
            AllDependencies()
                .Where(dependency => SameName(dependency.TargetObject, objectName))
                .Where(dependency => dependency.Operation == "SELECT"));
    }

    private string ExecuteImpactCommand(JsonElement parameters)
    {
        string objectName = RequiredCommandObjectName(parameters, "sqldeps.impact");
        ValidateIndexedObject(objectName);

        IReadOnlyList<ReachRow> rows = ReverseImpactRows(
            objectName,
            RepositoryReachability(AllDependencies().ToArray()));
        StringBuilder output = new();
        output.AppendLine($"Impact of changing: {objectName}");

        if (rows.Count == 0)
        {
            output.Append("none");

            return output.ToString();
        }

        foreach (ReachRow row in rows.Take(MaxDependencyRows))
        {
            output.AppendLine($"- {row.Source} (depth {row.Depth})");
            AppendImpactPath(output, row.Path);
        }

        AppendTruncation(output, rows.Count);

        return output.ToString().TrimEnd();
    }

    private string ExecuteCallersCommand(JsonElement parameters)
    {
        string objectName = RequiredCommandObjectName(parameters, "sqldeps.callers");
        ValidateIndexedObject(objectName);

        Dependency[] dependencies = AllDependencies().ToArray();
        ReachResult reachability = RepositoryReachability(dependencies);
        ReachRow[] rows = reachability
            .Reach
            .Where(row => SameName(row.Reached, objectName))
            .OrderBy(row => row.Depth)
            .ThenBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDependencyRows)
            .ToArray();
        StringBuilder output = new();
        output.AppendLine($"Callers of: {objectName}");

        if (rows.Length == 0)
        {
            output.Append("none");

            return output.ToString();
        }

        foreach (ReachRow row in rows)
        {
            output.AppendLine($"- {row.Source} (depth {row.Depth})");
            AppendImpactPath(output, row.Path);
            AppendCallerEvidence(output, row.Path, dependencies);
        }

        AppendCallerCycles(output, objectName, reachability);

        if (rows.Any(row => row.Depth >= MaxRepositoryTraversalDepth))
        {
            output.AppendLine($"Caller traversal may be truncated at depth {MaxRepositoryTraversalDepth}.");
        }

        return output.ToString().TrimEnd();
    }

    private static void AppendCallerEvidence(
        StringBuilder output,
        string path,
        IReadOnlyList<Dependency> dependencies)
    {
        var parts = ImpactPathStepPattern().Split(path);

        for (var index = 1; index + 1 < parts.Length; index += 2)
        {
            string source = parts[index - 1];
            string operation = parts[index];
            string target = parts[index + 1];
            Dependency? dependency = dependencies.FirstOrDefault(item =>
                SameName(item.SourceObject, source)
                && SameName(item.TargetObject, target)
                && string.Equals(item.Operation, operation, StringComparison.OrdinalIgnoreCase));

            if (dependency is null)
            {
                continue;
            }

            string dynamicSuffix = dependency.ViaDynamic ? ", dynamic" : "";
            output.AppendLine(
                $"    - {source} --{operation}--> {target} ({dependency.SourceFile}:{dependency.Line}{dynamicSuffix})");
        }
    }

    private static void AppendCallerCycles(
        StringBuilder output,
        string objectName,
        ReachResult reachability)
    {
        CycleRow[] cycles = reachability.Cycles
            .Where(cycle => SameName(cycle.ObjectName, objectName))
            .ToArray();

        foreach (CycleRow cycle in cycles)
        {
            output.AppendLine($"- cycle at {cycle.ObjectName} (depth {cycle.Depth})");
            AppendImpactPath(output, cycle.Path);
        }
    }

    /// <summary>Writes a dependency path on one line as "source › target (OPERATION) › target (OPERATION)".</summary>
    private static void AppendImpactPath(StringBuilder output, string path)
    {
        // Split keeps the captured operation: [source, operation, target, operation, target, ...].
        var parts = ImpactPathStepPattern().Split(path);
        StringBuilder line = new();
        line.Append("    ").Append(parts[0]);

        for (var index = 1; index + 1 < parts.Length; index += 2)
        {
            line.Append(" \u203A ").Append(parts[index + 1]).Append(" (").Append(parts[index]).Append(')');
        }

        output.AppendLine(line.ToString());
    }

    [GeneratedRegex(@"\s-(?<operation>[A-Za-z_]+)->\s")]
    private static partial Regex ImpactPathStepPattern();

    private string ExecuteColumnWritersCommand(JsonElement parameters)
    {
        string[] arguments = RequiredCommandArguments(parameters, "sqldeps.column-writers", 2);
        string tableName = Qualify(arguments[0].Trim().Replace("[", "").Replace("]", ""));
        string columnName = arguments[1].Trim().Replace("[", "").Replace("]", "");
        IndexQueryService queries = CreateRefactorImpactQueries()
            ?? throw new InvalidOperationException("No selected index is available for column writers.");

        if (!queries.IsIndexCurrent())
        {
            throw new InvalidOperationException("The selected index is stale; reindex before reading column writers.");
        }

        return FormatColumnWriterRows(
            $"Who writes: {tableName}.{columnName}",
            queries.WhoUpdates(tableName, columnName, MaxDependencyRows));
    }

    private void ValidateIndexedObject(string objectName)
    {
        string qualifiedName = Qualify(objectName.Trim().Replace("[", "").Replace("]", ""));

        if (!AllObjects().Any(item => SameName(item.Object.Name, qualifiedName)))
        {
            throw new ArgumentException($"No indexed object: {qualifiedName}");
        }
    }

    private static string RequiredCommandObjectName(JsonElement parameters, string command)
    {
        if (!TryGetCommandObjectName(parameters, out var objectName))
        {
            throw new ArgumentException($"{command} requires one object name argument.");
        }

        return objectName;
    }

    private static string[] RequiredCommandArguments(
        JsonElement parameters,
        string command,
        int count)
    {
        if (!TryGetStringArguments(parameters, count, out string[] values))
        {
            throw new ArgumentException($"{command} requires {count} non-empty string arguments.");
        }

        return values;
    }

    private static bool TryGetStringArguments(
        JsonElement parameters,
        int count,
        out string[] values)
    {
        values = [];

        if (!parameters.TryGetProperty("arguments", out JsonElement arguments))
        {
            return false;
        }

        if (arguments.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (arguments.GetArrayLength() != count)
        {
            return false;
        }

        List<string> parsedValues = [];

        foreach (JsonElement value in arguments.EnumerateArray())
        {
            if (!TryGetNonEmptyString(value, out string parsedValue))
            {
                return false;
            }

            parsedValues.Add(parsedValue);
        }

        values = parsedValues.ToArray();

        return true;
    }

    private static bool TryGetNonEmptyString(JsonElement value, out string text)
    {
        text = "";

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? valueText = value.GetString();
        if (string.IsNullOrWhiteSpace(valueText))
        {
            return false;
        }

        text = valueText;

        return true;
    }

    private static bool IsWriteOperation(string operation)
    {
        return operation is "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "TRUNCATE";
    }

    private static string FormatDependencyRows(
        string title,
        IEnumerable<Dependency> dependencies)
    {
        var groups = dependencies
            .GroupBy(dependency => dependency.SourceObject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        StringBuilder output = new();
        output.AppendLine(title);

        foreach (var group in groups.Take(MaxDependencyRows))
        {
            var operations = string.Join(", ", group
                .Select(dependency => dependency.Operation)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(operation => operation, StringComparer.OrdinalIgnoreCase));
            var locations = string.Join(", ", group
                .Select(dependency => $"{dependency.SourceFile}:{dependency.Line}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(location => location, StringComparer.OrdinalIgnoreCase));

            output.AppendLine($"- {group.Key} ({operations})");
            output.AppendLine($"  {locations}");
        }

        if (groups.Length == 0)
        {
            output.AppendLine("none");
        }

        AppendTruncation(output, groups.Length);

        return output.ToString().TrimEnd();
    }

    private static string FormatColumnWriterRows(
        string title,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        StringBuilder output = new();
        output.AppendLine(title);
        var groups = rows
            .GroupBy(row => ReadValue(row, "source_object"), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in groups)
        {
            string operations = string.Join(", ", group
                .Select(row => ReadValue(row, "operation"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(operation => operation, StringComparer.OrdinalIgnoreCase));
            string locations = string.Join(", ", group
                .Select(row => $"{ReadValue(row, "source_file")}:{ReadValue(row, "line")}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(location => location, StringComparer.OrdinalIgnoreCase));
            output.AppendLine($"- {group.Key} ({operations})");
            output.AppendLine($"  {locations}");
        }

        if (groups.Length == 0)
        {
            output.AppendLine("none");
        }

        AppendTruncation(output, groups.Length);

        return output.ToString().TrimEnd();
    }

    private static string ReadValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        return row.TryGetValue(key, out object? value) ? value?.ToString() ?? "" : "";
    }

    private static void AppendTruncation(StringBuilder output, int count)
    {
        if (count > MaxDependencyRows)
        {
            output.AppendLine($"... {count - MaxDependencyRows} more result(s) omitted.");
        }
    }
}
