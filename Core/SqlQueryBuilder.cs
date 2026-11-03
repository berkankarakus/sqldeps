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
        var distinctColumns = columns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctColumns.Length == 0)
        {
            throw new ArgumentException($"No indexed columns found for {objectName}.", nameof(columns));
        }

        var selectColumns = string.Join(",\n", distinctColumns.Select(column => $"    t.{QuoteIdentifier(column)}"));
        var sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(objectName)} AS t;\n-- Add TOP (n) or a WHERE condition as needed.";

        return new QueryPreview(objectName, objectKind, distinctColumns, sql);
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
