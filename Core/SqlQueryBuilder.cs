using Microsoft.SqlServer.TransactSql.ScriptDom;

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
        string[] distinctColumns = DistinctColumns(columns, objectName);

        string selectColumns = string.Join(",\n", distinctColumns.Select(column => $"    t.{QuoteIdentifier(column)}"));
        string sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(objectName)} AS t;\n-- Add TOP (n) or a WHERE condition as needed.";

        EnsureParseable(sql);

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
        string[] sourceColumnList = DistinctColumns(sourceColumns, sourceName);
        string[] targetColumnList = DistinctColumns(targetColumns, targetName);
        string[] columns = sourceColumnList
            .Select(column => $"t.{QuoteIdentifier(column)}")
            .Concat(targetColumnList.Select(column => $"r.{QuoteIdentifier(column)}"))
            .ToArray();
        string selectColumns = string.Join(",\n", columns.Select(column => $"    {column}"));
        string sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(sourceName)} AS t\n"
            + $"JOIN {QuoteMultipartIdentifier(targetName)} AS r ON r.{QuoteIdentifier(targetKey)} = t.{QuoteIdentifier(sourceKey)};\n"
            + "-- Add TOP (n) or a WHERE condition as needed.";

        EnsureParseable(sql);

        return new QueryPreview(sourceName, $"{sourceKind} + join", columns, sql);
    }

    private static void EnsureParseable(string sql)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(sql);
        parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Generated query preview could not be parsed safely.");
        }
    }

    private static string[] DistinctColumns(IEnumerable<string> columns, string objectName)
    {
        string[] distinctColumns = columns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctColumns.Length == 0)
        {
            throw new ArgumentException($"No indexed columns found for {objectName}.", nameof(columns));
        }

        return distinctColumns;
    }

    internal static string QuoteMultipartIdentifier(string name)
    {
        return string.Join('.', name.Split('.').Select(QuoteIdentifier));
    }

    internal static string QuoteIdentifier(string value)
    {
        return $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
