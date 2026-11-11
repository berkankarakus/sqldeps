using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportMissingRequiredInsertColumns(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            if (HasNoExplicitColumnsAndIsNotDefaultValues(node.InsertSpecification))
            {
                return;
            }

            var inserted = node.InsertSpecification.Columns
                .Select(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns.Where(IsRequiredInsertColumn).Where(column => !inserted.Contains(column.ColumnName)))
            {
                Add("missing-required-insert-column", node.InsertSpecification.Target, $"INSERT omits required column '{column.ColumnName}' from '{tableName}'.", "warning");
            }
        }

        private static bool HasNoExplicitColumnsAndIsNotDefaultValues(InsertSpecification specification)
        {
            if (specification.Columns.Count != 0)
            {
                return false;
            }

            return specification.InsertSource is not ValuesInsertSource { IsDefaultValues: true };
        }

        private static bool IsRequiredInsertColumn(TableColumn column) =>
            column.IsNullable == false
            && !column.IsIdentity
            && !column.IsComputed
            && column.DefaultValue is null;

        private void ReportIdentityInsert(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            var identityColumns = columns
                .Where(column => column.IsIdentity)
                .Select(column => column.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } identifier
                    && identityColumns.Contains(identifier.Value)))
            {
                Add("identity-insert", column, $"INSERT explicitly targets identity column '{column.MultiPartIdentifier!.Identifiers[^1].Value}'; verify that IDENTITY_INSERT is enabled.", "warning");
            }
        }

        private void ReportComputedInsert(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns))
            {
                return;
            }

            var computedColumns = columns
                .Where(column => column.IsComputed)
                .Select(column => column.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } identifier
                    && computedColumns.Contains(identifier.Value)))
            {
                Add("computed-insert", column, $"INSERT explicitly targets computed column '{column.MultiPartIdentifier!.Identifiers[^1].Value}'; computed columns cannot be assigned.");
            }
        }

        private void ValidateInsertColumns(
            InsertStatement node,
            string tableName,
            IReadOnlySet<string> knownColumns
        )
        {
            foreach (var column in node.InsertSpecification.Columns)
            {
                var columnName = column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;

                if (columnName is not null && !knownColumns.Contains(columnName))
                {
                    Add("unknown-insert-column", column, InvalidColumnMessage("INSERT column", columnName, tableName, knownColumns));
                }
            }
        }

        private void ReportDuplicateInsertColumns(InsertStatement node)
        {
            foreach (var duplicates in node.InsertSpecification.Columns
                .Where(column => column.MultiPartIdentifier?.Identifiers.Count > 0)
                .GroupBy(column => column.MultiPartIdentifier!.Identifiers[^1].Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var duplicate in duplicates.Skip(1))
                {
                    Add("duplicate-insert-column", duplicate, $"Column '{duplicates.Key}' appears more than once in this INSERT list.");
                }
            }
        }

        private void ReportInsertValues(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not ValuesInsertSource values)
            {
                return;
            }

            var targetCount = node.InsertSpecification.Columns.Count;
            var targetDescription = $"{targetCount} target columns";
            if (targetCount == 0)
            {
                if (values.IsDefaultValues)
                {
                    return;
                }

                if (node.InsertSpecification.Target is not NamedTableReference target)
                {
                    return;
                }

                if (!TryTableColumns(target, out var tableName, out _))
                {
                    return;
                }

                if (!metadataByTable.TryGetValue(tableName, out var columns))
                {
                    return;
                }

                if (columns.Any(column => column.IsIdentity || column.IsComputed))
                {
                    return;
                }

                targetCount = columns.Length;
                targetDescription = $"{targetCount} table columns";
            }

            foreach (var row in values.RowValues.Where(row => row.ColumnValues.Count != targetCount))
            {
                Add("insert-value-count", row, $"INSERT targets {targetDescription} but supplies {row.ColumnValues.Count} values.");
            }
        }

    }
}
