using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

/// <summary>Normalizes SQL fragments for conservative predicate comparisons.</summary>
internal static partial class SqlExpressionNormalizer
{
    public static string Normalize(string sql) => string.Concat(sql
        .Where(character => !char.IsWhiteSpace(character)))
        .Replace("[", "", StringComparison.Ordinal)
        .Replace("]", "", StringComparison.Ordinal)
        .ToUpperInvariant();

    public static bool IsUnicodeType(string dataType) => dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("NTEXT", StringComparison.OrdinalIgnoreCase);

    public static string RemoveUnicodeLiteralPrefixes(string sql) => UnicodeLiteralPrefixPattern().Replace(sql, "");

    public static IReadOnlyList<string> Collations(string sql) => CollationPattern().Matches(sql)
        .Select(match => match.Groups["name"].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string RemoveCollations(string sql) => CollationPattern().Replace(sql, "");

    public static string NormalizeCollation(string collation) => string.Concat(collation
        .Where(character => char.IsLetterOrDigit(character)))
        .ToUpperInvariant();

    [GeneratedRegex(@"N(?=')", RegexOptions.IgnoreCase)]
    private static partial Regex UnicodeLiteralPrefixPattern();

    [GeneratedRegex(@"\bCOLLATE\s+(?<name>[A-Za-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CollationPattern();
}
