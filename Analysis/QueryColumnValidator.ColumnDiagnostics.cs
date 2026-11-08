using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ValidateColumn(ColumnReferenceExpression column, IReadOnlyList<TableReference> tables)
        {
            if (!TryGetColumnParts(column, out var qualifier, out var columnName))
            {
                return;
            }

            if (qualifier is not null)
            {
                ValidateQualifiedColumn(column, qualifier, columnName, tables);
                return;
            }

            ValidateUnqualifiedColumn(column, columnName, tables);
        }

        private static bool TryGetColumnParts(
            ColumnReferenceExpression column,
            out string? qualifier,
            out string columnName
        )
        {
            qualifier = null;
            columnName = string.Empty;
            if (!TryGetSupportedColumnIdentifiers(column, out IList<Identifier> identifiers))
            {
                return false;
            }

            columnName = identifiers[^1].Value;
            if (identifiers.Count == 2)
            {
                qualifier = identifiers[0].Value;
            }

            return true;
        }

        private static bool TryGetSupportedColumnIdentifiers(
            ColumnReferenceExpression column,
            out IList<Identifier> identifiers
        )
        {
            IList<Identifier>? availableIdentifiers = column.MultiPartIdentifier?.Identifiers;
            if (availableIdentifiers is null)
            {
                identifiers = [];

                return false;
            }

            identifiers = availableIdentifiers;

            return identifiers.Count is >= 1 and <= 2;
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

            if (matchingTables.Length == 1)
            {
                return;
            }

            matchingTables = PreferCurrentScopeTables(matchingTables);
            if (matchingTables.Length == 1)
            {
                return;
            }

            Add("ambiguous-column", column, $"Ambiguous column '{columnName}': it exists in {string.Join(", ", matchingTables.Select(table => table.Qualifier))}. Qualify it with a table alias.");
        }

        private static TableReference[] PreferCurrentScopeTables(IReadOnlyList<TableReference> tables)
        {
            var localTables = tables
                .Where(table => !table.IsOuterScope)
                .ToArray();

            return localTables.Length > 0 ? localTables : tables.ToArray();
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

            return TryLocalTableReference(reference);
        }

        private TableReference? TryLocalTableReference(NamedTableReference reference)
        {
            var localName = reference.SchemaObject.BaseIdentifier.Value;
            var localColumns = LocalColumnsFor(localName, LineOf(reference));
            if (localColumns.Count == 0)
            {
                return null;
            }

            return new TableReference(reference.Alias?.Value, localName, localColumns, reference);
        }

        private void ReportAmbiguousTableName(NamedTableReference reference, string tableName)
        {
            if (reference.SchemaObject.SchemaIdentifier is not null)
            {
                return;
            }

            var baseName = reference.SchemaObject.BaseIdentifier.Value;
            if (!tablesByBaseName.TryGetValue(baseName, out var candidates))
            {
                return;
            }

            if (candidates.Length < 2)
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

            return TryCreateLocalTableReference(alias, alias, reference);
        }

        private TableReference? TryVariableTableReference(VariableTableReference reference)
        {
            var name = reference.Variable.Name;
            return TryCreateLocalTableReference(reference.Alias?.Value, name, reference);
        }

        private TableReference? TryCreateLocalTableReference(
            string? alias,
            string name,
            TSqlFragment reference
        )
        {
            var columns = LocalColumnsFor(name, LineOf(reference));
            if (columns.Count == 0)
            {
                return null;
            }

            return new TableReference(alias, name, columns, reference);
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
            var columns = OpenJsonColumns(reference);

            return new TableReference(reference.Alias?.Value, "OPENJSON", columns, reference);
        }

        private static IReadOnlySet<string> OpenJsonColumns(OpenJsonTableReference reference)
        {
            var declaredColumns = reference.SchemaDeclarationItems?
                .Select(item => item.ColumnDefinition?.ColumnIdentifier?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (declaredColumns is { Count: > 0 })
            {
                return declaredColumns;
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "key",
                "value",
                "type",
            };
        }

        private static TableReference? TryPivotedTableReference(PivotedTableReference reference)
        {
            var columns = reference.InColumns?
                .Select(column => column.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return CreateAliasedTableReference(reference.Alias?.Value, columns, reference);
        }

        private static TableReference? TryUnpivotedTableReference(UnpivotedTableReference reference)
        {
            var columns = UnpivotedColumns(reference);

            return CreateAliasedTableReference(reference.Alias?.Value, columns, reference);
        }

        private static IReadOnlySet<string> UnpivotedColumns(UnpivotedTableReference reference)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (reference.PivotColumn is not null)
            {
                columns.Add(reference.PivotColumn.Value);
            }

            if (reference.ValueColumn is not null)
            {
                columns.Add(reference.ValueColumn.Value);
            }

            return columns;
        }

        private static TableReference? CreateAliasedTableReference(
            string? alias,
            IReadOnlySet<string> columns,
            TSqlFragment reference
        )
        {
            if (alias is null || columns.Count == 0)
            {
                return null;
            }

            return new TableReference(alias, alias, columns, reference);
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
