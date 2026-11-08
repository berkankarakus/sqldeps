using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private bool FilteredPredicateMatches(string tableName, string predicate, string whereSql, string normalizedWhere)
        {
            string normalizedPredicate = SqlExpressionNormalizer.Normalize(predicate);

            if (ContainsNormalizedPredicate(normalizedWhere, normalizedPredicate))
            {
                return true;
            }

            if (!TryGetFilteredColumn(tableName, predicate, out var column))
            {
                return false;
            }

            (string comparablePredicate, string comparableWhere) = NormalizeFilteredPredicate(column, predicate, whereSql);

            if (!TryNormalizeMatchingCollation(column, ref comparablePredicate, ref comparableWhere))
            {
                return false;
            }

            return ContainsNormalizedPredicate(
                SqlExpressionNormalizer.Normalize(comparableWhere),
                SqlExpressionNormalizer.Normalize(comparablePredicate));
        }

        private static bool ContainsNormalizedPredicate(string normalizedWhere, string normalizedPredicate) =>
            normalizedWhere.Contains(normalizedPredicate, StringComparison.OrdinalIgnoreCase);

        private static (string Predicate, string Where) NormalizeFilteredPredicate(
            TableColumn column,
            string predicate,
            string where)
        {
            if (!SqlExpressionNormalizer.IsUnicodeType(column.DataType))
            {
                return (predicate, where);
            }

            return (
                SqlExpressionNormalizer.RemoveUnicodeLiteralPrefixes(predicate),
                SqlExpressionNormalizer.RemoveUnicodeLiteralPrefixes(where));
        }

        private bool TryGetFilteredColumn(string tableName, string predicate, out TableColumn column)
        {
            column = null!;
            if (!metadataByTable.TryGetValue(tableName, out var metadata))
            {
                return false;
            }

            var normalizedPredicate = SqlExpressionNormalizer.Normalize(predicate);
            var candidates = metadata
                .Where(candidate => Regex.IsMatch(
                    normalizedPredicate,
                    $"(?<![A-Z0-9_]){Regex.Escape(SqlExpressionNormalizer.Normalize(candidate.ColumnName))}(?![A-Z0-9_])",
                    RegexOptions.IgnoreCase))
                .ToArray();
            if (candidates.Length != 1)
            {
                return false;
            }

            column = candidates[0];
            return true;
        }

        private static bool TryNormalizeMatchingCollation(TableColumn column, ref string predicate, ref string where)
        {
            var predicateCollations = SqlExpressionNormalizer.Collations(predicate);
            var whereCollations = SqlExpressionNormalizer.Collations(where);
            if (predicateCollations.Count == 0 && whereCollations.Count == 0)
            {
                return true;
            }

            var allCollations = predicateCollations.Concat(whereCollations).ToArray();
            if (allCollations.Length > 1)
            {
                return false;
            }

            if (column.Collation is null)
            {
                return false;
            }

            if (!CollationsMatch(allCollations, column.Collation))
            {
                return false;
            }

            if (whereCollations.Count > 0 && !HasColumnCollation(where, column))
            {
                return false;
            }

            predicate = SqlExpressionNormalizer.RemoveCollations(predicate);
            where = SqlExpressionNormalizer.RemoveCollations(where);
            return true;
        }

        private static bool CollationsMatch(IEnumerable<string> collations, string columnCollation)
        {
            string normalizedColumnCollation = SqlExpressionNormalizer.NormalizeCollation(columnCollation);

            return collations.All(collation =>
                string.Equals(
                    SqlExpressionNormalizer.NormalizeCollation(collation),
                    normalizedColumnCollation,
                    StringComparison.Ordinal));
        }

        private static bool HasColumnCollation(string where, TableColumn column)
        {
            string normalizedWhere = SqlExpressionNormalizer.Normalize(where);
            string normalizedColumnName = SqlExpressionNormalizer.Normalize(column.ColumnName);
            string pattern = $"(?:^|AND|OR|\\(){Regex.Escape(normalizedColumnName)}COLLATE";

            return Regex.IsMatch(normalizedWhere, pattern, RegexOptions.IgnoreCase);
        }

    }
}
