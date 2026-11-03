using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(BooleanComparisonExpression node)
        {
            ReportImplicitFilterConversion(node);
            ReportForeignKeyJoinMismatch(node);
            ReportJoinTypeMismatch(node);
            base.Visit(node);
        }

        /// <summary>Reports only deterministic type-family or collation mismatches in JOIN equality predicates.</summary>
        private void ReportJoinTypeMismatch(BooleanComparisonExpression comparison)
        {
            if (comparison.ComparisonType != BooleanComparisonType.Equals
                || comparison.FirstExpression is not ColumnReferenceExpression first
                || comparison.SecondExpression is not ColumnReferenceExpression second)
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
            if (!TryGetColumnMetadata(first, tables, out var firstMetadata)
                || !TryGetColumnMetadata(second, tables, out var secondMetadata)
                || !AreIncompatible(firstMetadata, secondMetadata))
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

            if (firstFamily != secondFamily && firstFamily != "other" && secondFamily != "other")
            {
                return true;
            }

            return firstFamily == "string"
                && secondFamily == "string"
                && first.Collation is not null
                && second.Collation is not null
                && !string.Equals(SqlExpressionNormalizer.NormalizeCollation(first.Collation), SqlExpressionNormalizer.NormalizeCollation(second.Collation), StringComparison.Ordinal);
        }

        private static string TypeFamily(string dataType)
        {
            if (IsNumericType(dataType))
            {
                return "numeric";
            }

            if (dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase))
            {
                return "datetime";
            }

            if (dataType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                return "string";
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return "guid";
            }

            return "other";
        }

        private void ReportForeignKeyJoinMismatch(BooleanComparisonExpression comparison)
        {
            if (comparison.ComparisonType != BooleanComparisonType.Equals
                || comparison.FirstExpression is not ColumnReferenceExpression first
                || comparison.SecondExpression is not ColumnReferenceExpression second)
            {
                return;
            }

            var query = allQueries.Where(candidate => Contains(candidate, comparison)).OrderBy(candidate => candidate.FragmentLength).FirstOrDefault();
            if (query is null)
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryFindTable(first, tables, out var firstTable)
                || !TryFindTable(second, tables, out var secondTable))
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

            if (string.Equals(foreignKey.ReferencedTableName, otherTable.Name, StringComparison.OrdinalIgnoreCase)
                && otherTable.Columns.Contains(foreignKey.ReferencedColumnName))
            {
                return;
            }

            Add("foreign-key-join-mismatch", column, $"Foreign key '{table.Name}.{columnName}' references '{foreignKey.ReferencedTableName}.{foreignKey.ReferencedColumnName}', but this JOIN uses '{otherTable.Name}'.", "info");
        }

        private void ReportImplicitFilterConversion(BooleanComparisonExpression node)
        {
            if (!TryGetColumnAndLiteral(node, out var column, out var literal))
            {
                return;
            }

            var query = allQueries
                .Where(candidate => Contains(candidate, node))
                .OrderBy(candidate => candidate.FragmentLength)
                .FirstOrDefault();

            if (query is null)
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryGetColumnMetadata(column, tables, out var metadata)
                || !IsStringConversionTarget(metadata.DataType))
            {
                return;
            }

            Add(
                "implicit-filter-conversion",
                literal,
                $"String literal '{literal.Value}' is compared with '{metadata.DataType}' column '{column.MultiPartIdentifier!.Identifiers[^1].Value}' and may require an implicit conversion.",
                "info");
        }

        private static bool TryGetColumnAndLiteral(
            BooleanComparisonExpression comparison,
            out ColumnReferenceExpression column,
            out StringLiteral literal)
        {
            if (comparison.FirstExpression is ColumnReferenceExpression firstColumn
                && comparison.SecondExpression is StringLiteral secondLiteral)
            {
                column = firstColumn;
                literal = secondLiteral;

                return true;
            }

            if (comparison.SecondExpression is ColumnReferenceExpression secondColumn
                && comparison.FirstExpression is StringLiteral firstLiteral)
            {
                column = secondColumn;
                literal = firstLiteral;

                return true;
            }

            column = null!;
            literal = null!;

            return false;
        }

        private bool TryGetColumnMetadata(
            ColumnReferenceExpression column,
            IReadOnlyList<TableReference> tables,
            out TableColumn metadata)
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;
            var columnName = identifiers?.LastOrDefault()?.Value;

            if (identifiers is null || columnName is null)
            {
                metadata = null!;

                return false;
            }

            var candidates = identifiers.Count == 2
                ? tables.Where(table => table.Matches(identifiers[0].Value))
                : tables;
            var matches = candidates
                .SelectMany(table => metadataByTable.GetValueOrDefault(table.Name) ?? [])
                .Where(item => string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length != 1)
            {
                metadata = null!;

                return false;
            }

            metadata = matches[0];

            return true;
        }


    }
}
