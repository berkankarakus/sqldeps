using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private bool FilteredPredicateMatches(string tableName, string predicate, string whereSql, string normalizedWhere)
        {
            var normalizedPredicate = SqlExpressionNormalizer.Normalize(predicate);
            if (normalizedWhere.Contains(normalizedPredicate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!TryGetFilteredColumn(tableName, predicate, out var column))
            {
                return false;
            }

            var comparablePredicate = predicate;
            var comparableWhere = whereSql;
            if (SqlExpressionNormalizer.IsUnicodeType(column.DataType))
            {
                comparablePredicate = SqlExpressionNormalizer.RemoveUnicodeLiteralPrefixes(comparablePredicate);
                comparableWhere = SqlExpressionNormalizer.RemoveUnicodeLiteralPrefixes(comparableWhere);
            }

            if (!TryNormalizeMatchingCollation(column, ref comparablePredicate, ref comparableWhere))
            {
                return false;
            }

            return SqlExpressionNormalizer.Normalize(comparableWhere).Contains(SqlExpressionNormalizer.Normalize(comparablePredicate), StringComparison.OrdinalIgnoreCase);
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

            if (!allCollations.All(collation => string.Equals(SqlExpressionNormalizer.NormalizeCollation(collation), SqlExpressionNormalizer.NormalizeCollation(column.Collation), StringComparison.Ordinal)))
            {
                return false;
            }

            if (whereCollations.Count > 0
                && !Regex.IsMatch(
                    SqlExpressionNormalizer.Normalize(where),
                    $"(?:^|AND|OR|\\(){Regex.Escape(SqlExpressionNormalizer.Normalize(column.ColumnName))}COLLATE",
                    RegexOptions.IgnoreCase))
            {
                return false;
            }

            predicate = SqlExpressionNormalizer.RemoveCollations(predicate);
            where = SqlExpressionNormalizer.RemoveCollations(where);
            return true;
        }

    }
}
