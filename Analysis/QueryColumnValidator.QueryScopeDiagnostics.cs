using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportQueryScopeDiagnostics(
            QuerySpecification query,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlyList<TableReference> tables)
        {
            ReportCteAliasShadowing(tables);
            ReportUnknownTables(query, nestedQueries);
        }

        private void ReportUnknownTables(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries)
        {
            if (columnsByTable.Count == 0)
            {
                return;
            }

            foreach (var table in TableReferenceFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries)))
            {
                var tableName = ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema);
                var localName = table.SchemaObject.BaseIdentifier.Value;

                if (IsKnownTable(table, tableName, localName))
                {
                    continue;
                }

                var severity = table.SchemaObject.SchemaIdentifier is null ? "info" : "warning";
                Add("unknown-table", table, $"Table or view '{tableName}' was not found in the workspace index.", severity);
            }
        }

        private bool IsKnownTable(NamedTableReference table, string tableName, string localName)
        {
            if (columnsByTable.ContainsKey(tableName))
            {
                return true;
            }

            if (HasLocalColumns(localName, LineOf(table)))
            {
                return true;
            }

            if (cteNames.Contains(localName))
            {
                return true;
            }

            return ObjectNames.IsTemporary(table.SchemaObject);
        }

        private void ValidateQueryColumns(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlySet<ColumnReferenceExpression> orderColumns,
            IReadOnlyList<TableReference> tables,
            bool hasUnresolvedDirectTables)
        {
            foreach (var column in ColumnReferenceFinder.Find(node)
                .Where(column => !IsNested(column, nestedQueries) && !orderColumns.Contains(column)))
            {
                ValidateColumn(column, tables, hasUnresolvedDirectTables);
            }
        }

        private bool HasUnresolvedDirectTables(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries)
        {
            foreach (var table in TableReferenceFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries)))
            {
                var tableName = ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema);
                var localName = table.SchemaObject.BaseIdentifier.Value;

                if (!IsKnownTable(table, tableName, localName))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportCteAliasShadowing(IReadOnlyList<TableReference> tables)
        {
            foreach (var table in tables.Where(table => table.Alias is not null && cteNames.Contains(table.Alias)))
            {
                Add("cte-alias-shadowing", table.Node, $"Table alias '{table.Alias}' shadows a CTE with the same name in this query scope.");
            }
        }
    }
}
