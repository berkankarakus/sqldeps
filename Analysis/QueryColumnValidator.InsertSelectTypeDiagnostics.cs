using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportInsertSelectTypes(InsertStatement node, string tableName)
        {
            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            if (!metadataByTable.TryGetValue(tableName, out var targetColumns))
            {
                return;
            }

            var targetColumnsByName = targetColumns.ToDictionary(
                column => column.ColumnName,
                StringComparer.OrdinalIgnoreCase);
            var tables = TablesFor(query, NestedQueries(query));
            foreach (var pair in node.InsertSpecification.Columns.Zip(query.SelectElements))
            {
                if (pair.Second is not SelectScalarExpression scalar)
                {
                    continue;
                }

                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null || !targetColumnsByName.TryGetValue(targetName, out var targetColumn))
                {
                    continue;
                }

                if (scalar.Expression is ColumnReferenceExpression sourceColumn
                    && TryGetTypeFamilyMismatch(
                        targetName,
                        sourceColumn,
                        targetColumnsByName,
                        tables,
                        out _,
                        out var sourceMetadata))
                {
                    Add(
                        "insert-select-type",
                        sourceColumn,
                        $"INSERT SELECT source column '{sourceMetadata.ColumnName}' has type '{sourceMetadata.DataType}', but target column '{targetName}' has type '{targetColumn.DataType}'.",
                        "info");
                }

                if (!TryGetComputedTypeFamily(scalar.Expression, tables, out var sourceFamily)
                    || string.Equals(TypeFamily(targetColumn.DataType), sourceFamily, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Add(
                    "insert-select-type",
                    scalar.Expression,
                    $"INSERT SELECT {ComputedExpressionName(scalar.Expression)} expression has type family '{sourceFamily}', but target column '{targetName}' has type '{targetColumn.DataType}'.",
                    "info");
            }
        }
    }
}
