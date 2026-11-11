using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

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

        private bool TryGetTypeFamilyMismatch(
            string targetColumnName,
            ColumnReferenceExpression sourceColumn,
            IReadOnlyDictionary<string, TableColumn> targetColumnsByName,
            IReadOnlyList<TableReference> tables,
            out TableColumn targetColumn,
            out TableColumn sourceMetadata)
        {
            targetColumn = null!;
            sourceMetadata = null!;

            if (!targetColumnsByName.TryGetValue(targetColumnName, out var candidate)
                || !TryGetColumnMetadata(sourceColumn, tables, out var source))
            {
                return false;
            }

            targetColumn = candidate;
            sourceMetadata = source;

            return TypeFamiliesDiffer(targetColumn.DataType, sourceMetadata.DataType);
        }

        private static string TypeFamily(string dataType)
        {
            if (IsNumericType(dataType))
            {
                return "numeric";
            }

            if (IsDateOrTimeType(dataType))
            {
                return "datetime";
            }

            if (IsStringType(dataType))
            {
                return "string";
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return "guid";
            }

            return "other";
        }

        private static bool IsDateOrTimeType(string dataType)
        {
            return dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStringType(string dataType)
        {
            if (dataType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return dataType.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase);
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
            if (!TryGetColumnMetadata(column, tables, out var metadata))
            {
                return;
            }

            if (!IsStringConversionTarget(metadata.DataType))
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
