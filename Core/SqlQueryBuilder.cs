namespace SqlDeps.Core;

public sealed record QueryPreview(
    string ObjectName,
    string ObjectKind,
    IReadOnlyList<string> Columns,
    string Sql
);

public static class SqlQueryBuilder
{
    public static QueryPreview BuildSelect(string objectName, string objectKind, IEnumerable<string> columns)
    {
        var distinctColumns = DistinctColumns(columns, objectName);

        var selectColumns = string.Join(",\n", distinctColumns.Select(column => $"    t.{QuoteIdentifier(column)}"));
        var sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(objectName)} AS t;\n-- Add TOP (n) or a WHERE condition as needed.";

        return new QueryPreview(objectName, objectKind, distinctColumns, sql);
    }

    public static QueryPreview BuildSelectWithJoin(
        string sourceName,
        string sourceKind,
        IEnumerable<string> sourceColumns,
        string targetName,
        IEnumerable<string> targetColumns,
        string sourceKey,
        string targetKey)
    {
        var sourceColumnList = DistinctColumns(sourceColumns, sourceName);
        var targetColumnList = DistinctColumns(targetColumns, targetName);
        var columns = sourceColumnList
            .Select(column => $"t.{QuoteIdentifier(column)}")
            .Concat(targetColumnList.Select(column => $"r.{QuoteIdentifier(column)}"))
            .ToArray();
        var selectColumns = string.Join(",\n", columns.Select(column => $"    {column}"));
        var sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(sourceName)} AS t\n"
            + $"JOIN {QuoteMultipartIdentifier(targetName)} AS r ON r.{QuoteIdentifier(targetKey)} = t.{QuoteIdentifier(sourceKey)};\n"
            + "-- Add TOP (n) or a WHERE condition as needed.";

        return new QueryPreview(sourceName, $"{sourceKind} + join", columns, sql);
    }

    private static string[] DistinctColumns(IEnumerable<string> columns, string objectName)
    {
        var distinctColumns = columns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctColumns.Length == 0)
        {
            throw new ArgumentException($"No indexed columns found for {objectName}.", nameof(columns));
        }

        return distinctColumns;
    }

    private static string QuoteMultipartIdentifier(string name)
    {
        return string.Join('.', name.Split('.').Select(QuoteIdentifier));
    }

    private static string QuoteIdentifier(string value)
    {
        return $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
