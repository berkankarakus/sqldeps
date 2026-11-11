using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        /// <summary>Reports only deterministic type-family or collation mismatches in JOIN equality predicates.</summary>
        private void ReportJoinTypeMismatch(BooleanComparisonExpression comparison)
        {
            if (!TryGetColumnEquality(comparison, out var first, out var second))
            {
                return;
            }

            var query = allQueries
                .Where(candidate => Contains(candidate, comparison))
                .OrderBy(candidate => candidate.FragmentLength)
                .FirstOrDefault();

            if (query is null)
            {
                return;
            }

            var joinComparisons = new JoinComparisonVisitor();
            query.FromClause?.Accept(joinComparisons);

            if (!joinComparisons.Comparisons.Contains(comparison))
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryGetIncompatibleColumnMetadata(first, second, tables, out var firstMetadata, out var secondMetadata))
            {
                return;
            }

            Add("join-type-mismatch", first, $"JOIN compares '{firstMetadata.DataType}' column '{firstMetadata.ColumnName}' with '{secondMetadata.DataType}' column '{secondMetadata.ColumnName}'.", "info");
        }

        /// <summary>Determines whether metadata differs in a way that can cause a lossy JOIN conversion.</summary>
        private static bool AreIncompatible(TableColumn first, TableColumn second)
        {
            var firstFamily = TypeFamily(first.DataType);
            var secondFamily = TypeFamily(second.DataType);

            if (HasKnownFamilyConflict(firstFamily, secondFamily))
            {
                return true;
            }

            return HasCollationMismatch(firstFamily, secondFamily, first, second);
        }

        private static bool HasKnownFamilyConflict(string firstFamily, string secondFamily)
        {
            return firstFamily != secondFamily
                && firstFamily != "other"
                && secondFamily != "other";
        }

        private void ReportForeignKeyJoinMismatch(BooleanComparisonExpression comparison)
        {
            if (!TryGetColumnEquality(comparison, out var first, out var second))
            {
                return;
            }

            var query = allQueries.Where(candidate => Contains(candidate, comparison)).OrderBy(candidate => candidate.FragmentLength).FirstOrDefault();
            if (query is null)
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryResolveJoinTables(first, second, tables, out var firstTable, out var secondTable))
            {
                return;
            }

            ReportForeignKeySide(first, firstTable, secondTable);
            ReportForeignKeySide(second, secondTable, firstTable);
        }

        private void ReportForeignKeySide(ColumnReferenceExpression column, TableReference table, TableReference otherTable)
        {
            var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
            if (!foreignKeysByColumn.TryGetValue((table.Name, columnName), out var foreignKey))
            {
                return;
            }

            if (MatchesReferencedForeignKey(foreignKey, otherTable))
            {
                return;
            }

            Add("foreign-key-join-mismatch", column, $"Foreign key '{table.Name}.{columnName}' references '{foreignKey.ReferencedTableName}.{foreignKey.ReferencedColumnName}', but this JOIN uses '{otherTable.Name}'.", "info");
        }

        private static bool MatchesReferencedForeignKey(TableForeignKey foreignKey, TableReference otherTable)
        {
            return string.Equals(
                       foreignKey.ReferencedTableName,
                       otherTable.Name,
                       StringComparison.OrdinalIgnoreCase)
                && otherTable.Columns.Contains(foreignKey.ReferencedColumnName);
        }

        private static bool TryGetColumnEquality(
            BooleanComparisonExpression comparison,
            out ColumnReferenceExpression first,
            out ColumnReferenceExpression second)
        {
            first = null!;
            second = null!;

            if (comparison.ComparisonType != BooleanComparisonType.Equals)
            {
                return false;
            }

            if (comparison.FirstExpression is not ColumnReferenceExpression firstColumn)
            {
                return false;
            }

            if (comparison.SecondExpression is not ColumnReferenceExpression secondColumn)
            {
                return false;
            }

            first = firstColumn;
            second = secondColumn;

            return true;
        }

        private bool TryGetIncompatibleColumnMetadata(
            ColumnReferenceExpression first,
            ColumnReferenceExpression second,
            IReadOnlyList<TableReference> tables,
            out TableColumn firstMetadata,
            out TableColumn secondMetadata)
        {
            firstMetadata = null!;
            secondMetadata = null!;

            if (!TryGetColumnMetadata(first, tables, out firstMetadata))
            {
                return false;
            }

            if (!TryGetColumnMetadata(second, tables, out secondMetadata))
            {
                return false;
            }

            return AreIncompatible(firstMetadata, secondMetadata);
        }

        private bool TryResolveJoinTables(
            ColumnReferenceExpression first,
            ColumnReferenceExpression second,
            IReadOnlyList<TableReference> tables,
            out TableReference firstTable,
            out TableReference secondTable)
        {
            firstTable = null!;
            secondTable = null!;

            if (!TryFindTable(first, tables, out firstTable))
            {
                return false;
            }

            if (!TryFindTable(second, tables, out secondTable))
            {
                return false;
            }

            return true;
        }

        private static bool HasCollationMismatch(string firstFamily, string secondFamily, TableColumn first, TableColumn second)
        {
            if (firstFamily != "string" || secondFamily != "string")
            {
                return false;
            }

            if (first.Collation is null || second.Collation is null)
            {
                return false;
            }

            return !string.Equals(
                SqlExpressionNormalizer.NormalizeCollation(first.Collation),
                SqlExpressionNormalizer.NormalizeCollation(second.Collation),
                StringComparison.Ordinal);
        }
    }
}
