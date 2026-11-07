using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>A parse-validated SQL preview for one indexed object or relationship.</summary>
/// <param name="ObjectName">The primary indexed object.</param>
/// <param name="ObjectKind">The indexed kind or relationship description.</param>
/// <param name="Columns">The columns included in the generated preview.</param>
/// <param name="Sql">The parse-validated read-only SQL.</param>
public sealed record QueryPreview(
    string ObjectName,
    string ObjectKind,
    IReadOnlyList<string> Columns,
    string Sql
);

/// <summary>Builds read-only T-SQL query previews from indexed metadata.</summary>
public static class SqlQueryBuilder
{
    /// <summary>Builds a single-object <c>SELECT</c> preview using the supplied columns.</summary>
    /// <param name="objectName">The table or view name to query.</param>
    /// <param name="objectKind">The indexed object kind shown to callers.</param>
    /// <param name="columns">The columns to include, in display order.</param>
    public static QueryPreview BuildSelect(string objectName, string objectKind, IEnumerable<string> columns)
    {
        string[] distinctColumns = DistinctColumns(columns, objectName);

        string selectColumns = string.Join(",\n", distinctColumns.Select(column => $"    t.{QuoteIdentifier(column)}"));
        string sql = $"SELECT\n{selectColumns}\nFROM {QuoteMultipartIdentifier(objectName)} AS t;\n-- Add TOP (n) or a WHERE condition as needed.";

        EnsureParseable(sql);

        return new QueryPreview(objectName, objectKind, distinctColumns, sql);
    }

    /// <summary>Builds a two-source join preview using the supplied key columns.</summary>
    /// <param name="sourceName">The primary table or view name.</param>
    /// <param name="sourceKind">The indexed kind of the primary object.</param>
    /// <param name="sourceColumns">Columns selected from the primary object.</param>
    /// <param name="targetName">The related table or view name.</param>
    /// <param name="targetColumns">Columns selected from the related object.</param>
    /// <param name="sourceKey">The join key on the primary object.</param>
    /// <param name="targetKey">The join key on the related object.</param>
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
