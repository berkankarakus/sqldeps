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
