using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ValidateColumn(
            ColumnReferenceExpression column,
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables
        )
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

            ValidateUnqualifiedColumn(column, columnName, tables, hasUnresolvedDirectTables);
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
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables
        )
        {
            var matchingTables = tables.Where(table => table.Columns.Contains(columnName)).ToArray();

            if (matchingTables.Length == 0)
            {
                ReportMissingUnqualifiedColumn(column, columnName, tables, hasUnresolvedDirectTables);
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
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables
        )
        {
            if (hasUnresolvedDirectTables || tables.Count != 1)
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

    }
}
