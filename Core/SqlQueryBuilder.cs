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
        var distinctColumns = DistinctColumns(columns, objectName);

        var selectColumns = string.Join(",\n", distinctColumns.Select(column => $"    t.{QuoteIdentifier(column)}"));
        var sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(objectName)} AS t;\n-- Add TOP (n) or a WHERE condition as needed.";

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

        EnsureParseable(sql);

        return new QueryPreview(sourceName, $"{sourceKind} + join", columns, sql);
    }

    private static void EnsureParseable(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out IList<ParseError> errors);


        if (errors.Count > 0)
        {

            throw new InvalidOperationException("Generated query preview could not be parsed safely.");
        }
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

    internal static string QuoteMultipartIdentifier(string name)
    {
        return string.Join('.', name.Split('.').Select(QuoteIdentifier));
    }

    internal static string QuoteIdentifier(string value)
    {
        return $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
