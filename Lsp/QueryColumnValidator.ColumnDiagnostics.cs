using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ValidateColumn(ColumnReferenceExpression column, IReadOnlyList<TableReference> tables)
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;
            if (identifiers is null || identifiers.Count is < 1 or > 2)
            {
                return;
            }

            var columnName = identifiers[^1].Value;
            if (identifiers.Count == 2)
            {
                ValidateQualifiedColumn(column, identifiers[0].Value, columnName, tables);
                return;
            }

            ValidateUnqualifiedColumn(column, columnName, tables);
        }

        private void ValidateQualifiedColumn(
            ColumnReferenceExpression column,
            string qualifier,
            string columnName,
            IReadOnlyList<TableReference> tables
        )
        {
            var matchingTables = tables.Where(table => table.Matches(qualifier)).ToArray();

            if (matchingTables.Length != 1)
            {
                return;
            }

            var table = matchingTables[0];

            if (table.Columns.Contains(columnName))
            {
                return;
            }

            Add("unknown-column", column, InvalidColumnMessage("column name", columnName, table.Name, table.Columns));
        }

        private void ValidateUnqualifiedColumn(
            ColumnReferenceExpression column,
            string columnName,
            IReadOnlyList<TableReference> tables
        )
        {
            var matchingTables = tables.Where(table => table.Columns.Contains(columnName)).ToArray();

            if (matchingTables.Length == 0)
            {
                ReportMissingUnqualifiedColumn(column, columnName, tables);
                return;
            }

            if (matchingTables.Length > 1)
            {
                var localTables = matchingTables
                    .Where(table => !table.IsOuterScope)
                    .ToArray();
                if (localTables.Length == 1)
                {
                    return;
                }

                if (localTables.Length > 1)
                {
                    matchingTables = localTables;
                }

                Add("ambiguous-column", column, $"Ambiguous column '{columnName}': it exists in {string.Join(", ", matchingTables.Select(table => table.Qualifier))}. Qualify it with a table alias.");
            }
        }

        private void ReportMissingUnqualifiedColumn(
            ColumnReferenceExpression column,
            string columnName,
            IReadOnlyList<TableReference> tables
        )
        {
            if (tables.Count != 1)
            {
                return;
            }

            var table = tables[0];
            Add("unknown-column", column, InvalidColumnMessage("column name", columnName, table.Name, table.Columns));
        }

        private static string InvalidColumnMessage(
            string kind,
            string columnName,
            string tableName,
            IEnumerable<string> knownColumns
        )
        {
            var message = $"Invalid {kind} '{columnName}' on '{tableName}'.";
            var suggestion = ColumnNameSuggestion.For(columnName, knownColumns);

            if (suggestion is null)
            {
                return message;
            }

            return $"{message} Did you mean '{suggestion}'?";
        }

        private bool TryTableColumns(NamedTableReference target, out string tableName, out HashSet<string> columns)
        {
            tableName = ObjectNames.Format(target.SchemaObject, ObjectNames.StandardDefaultSchema);
            return columnsByTable.TryGetValue(tableName, out columns!);
        }

        private TableReference? TryTableReference(NamedTableReference reference)
        {
            var tableName = ObjectNames.Format(reference.SchemaObject, ObjectNames.StandardDefaultSchema);

            ReportAmbiguousTableName(reference, tableName);

            if (columnsByTable.TryGetValue(tableName, out var columns))
            {
                return new TableReference(reference.Alias?.Value, tableName, columns, reference);
            }

            var localName = reference.SchemaObject.BaseIdentifier.Value;
            var localColumns = LocalColumnsFor(localName, LineOf(reference));

            return localColumns.Count > 0
                ? new TableReference(reference.Alias?.Value, localName, localColumns, reference)
                : null;
        }

        private void ReportAmbiguousTableName(NamedTableReference reference, string tableName)
        {
            if (reference.SchemaObject.SchemaIdentifier is not null)
            {
                return;
            }

            var baseName = reference.SchemaObject.BaseIdentifier.Value;
            if (!tablesByBaseName.TryGetValue(baseName, out var candidates)
                || candidates.Length < 2)
            {
                return;
            }

            Add("ambiguous-table", reference, $"Table '{baseName}' exists under multiple schemas ({string.Join(", ", candidates)}); qualify it as '{tableName}'.");
        }

        private TableReference? TryDerivedTableReference(QueryDerivedTable reference)
        {
            var alias = reference.Alias?.Value;

            if (alias is null)
            {
                return null;
            }

            var columns = LocalColumnsFor(alias, LineOf(reference));

            if (columns.Count == 0)
            {
                return null;
            }

            return new TableReference(alias, alias, columns, reference);
        }

        private TableReference? TryVariableTableReference(VariableTableReference reference)
        {
            var name = reference.Variable.Name;
            var columns = LocalColumnsFor(name, LineOf(reference));

            return columns.Count > 0
                ? new TableReference(reference.Alias?.Value, name, columns, reference)
                : null;
        }

        private TableReference? TryFunctionTableReference(SchemaObjectFunctionTableReference reference)
        {
            var functionName = ObjectNames.Format(reference.SchemaObject, ObjectNames.StandardDefaultSchema);
            var columns = resultColumnsByObject.GetValueOrDefault(functionName)?
                .Where(column => column.IsKnownName)
                .Select(column => column.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return columns is { Count: > 0 }
                ? new TableReference(reference.Alias?.Value, functionName, columns, reference)
                : null;
        }

        private TableReference? TryOpenJsonTableReference(OpenJsonTableReference reference)
        {
            var columns = reference.SchemaDeclarationItems?
                .Select(item => item.ColumnDefinition?.ColumnIdentifier?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (columns is null or { Count: 0 })
            {
                columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "key",
                    "value",
                    "type",
                };
            }

            return new TableReference(reference.Alias?.Value, "OPENJSON", columns, reference);
        }

        private static TableReference? TryPivotedTableReference(PivotedTableReference reference)
        {
            var alias = reference.Alias?.Value;
            var columns = reference.InColumns?
                .Select(column => column.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return alias is not null && columns is { Count: > 0 }
                ? new TableReference(alias, alias, columns, reference)
                : null;
        }

        private static TableReference? TryUnpivotedTableReference(UnpivotedTableReference reference)
        {
            var alias = reference.Alias?.Value;
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (reference.PivotColumn is not null)
            {
                columns.Add(reference.PivotColumn.Value);
            }

            if (reference.ValueColumn is not null)
            {
                columns.Add(reference.ValueColumn.Value);
            }

            return alias is not null && columns.Count > 0
                ? new TableReference(alias, alias, columns, reference)
                : null;
        }

        private bool HasLocalColumns(string alias, int line)
        {
            return LocalColumnsFor(alias, line).Count > 0;
        }

        private IReadOnlySet<string> LocalColumnsFor(string alias, int line)
        {
            if (!columnsByLocalName.TryGetValue(alias, out var columns))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var latestDeclaration = columns
                .Where(column => column.ScopeLine == _scopeLine)
                .Where(column => column.Line <= line)
                .GroupBy(column => column.Line)
                .OrderByDescending(group => group.Key)
                .FirstOrDefault();

            if (latestDeclaration is null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (latestDeclaration.Any(column => column.IsDrop))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return latestDeclaration
                .Select(column => column.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }


    }
}
