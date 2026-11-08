using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportQueryIndexDiagnostics(QuerySpecification query, IReadOnlyList<TableReference> tables)
        {
            ReportUnindexedWhere(query, tables);
            ReportUnindexedOrderBy(query, tables);
            ReportUnindexedJoins(query, tables);
        }

        private void ReportQueryOrderDiagnostics(
            QuerySpecification query,
            IReadOnlySet<ColumnReferenceExpression> orderColumns,
            IReadOnlyList<TableReference> tables
        )
        {
            var selectAliases = SelectAliases(query);
            ReportDistinctOrderBy(query, orderColumns, selectAliases);
            ReportOrderByOrdinals(query);
            ValidateOrderByColumns(orderColumns, selectAliases, tables);
        }

        private static HashSet<string> SelectAliases(QuerySpecification query) => query.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(element => element.ColumnName?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private void ReportUnindexedWhere(QuerySpecification query, IReadOnlyList<TableReference> tables)
        {
            if (query.WhereClause is null)
            {
                return;
            }

            foreach (var tableGroup in FindWhereColumns(query.WhereClause.SearchCondition, tables)
                .GroupBy(item => item.Table.Name, StringComparer.OrdinalIgnoreCase))
            {
                HashSet<string> indexedColumns = IndexedColumnsFor(tableGroup.Key, query.WhereClause.SearchCondition);

                foreach ((ColumnReferenceExpression column, TableReference table) in tableGroup)
                {
                    string columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
                    if (indexedColumns.Contains(columnName))
                    {
                        continue;
                    }

                    Add("unindexed-where", column, $"WHERE filters '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
                }
            }
        }

        private static IEnumerable<(ColumnReferenceExpression Column, TableReference Table)> FindWhereColumns(
            BooleanExpression where,
            IReadOnlyList<TableReference> tables)
        {
            foreach (ColumnReferenceExpression column in ColumnReferenceFinder.Find(where))
            {
                if (!TryFindTable(column, tables, out TableReference table))
                {
                    continue;
                }

                yield return (column, table);
            }
        }

        private void ReportUnindexedOrderBy(QuerySpecification query, IReadOnlyList<TableReference> tables)
        {
            if (query.OrderByClause is null)
            {
                return;
            }

            foreach (var column in query.OrderByClause.OrderByElements
                .Select(element => element.Expression)
                .OfType<ColumnReferenceExpression>())
            {
                if (!TryFindUnindexedTableColumn(column, tables, out var table))
                {
                    continue;
                }

                Add("unindexed-order-by", column, $"ORDER BY sorts '{table.Name}.{column.MultiPartIdentifier.Identifiers[^1].Value}', but no matching key/index column is known.", "info");
            }
        }

        private void ReportUnindexedJoins(QuerySpecification query, IReadOnlyList<TableReference> tables)
        {
            if (query.FromClause is null)
            {
                return;
            }

            JoinComparisonVisitor visitor = new();
            query.FromClause.Accept(visitor);

            foreach (var comparison in visitor.Comparisons)
            {
                if (!TryResolveJoinOperands(comparison, tables, out var first, out var second, out var firstTable, out var secondTable))
                {
                    continue;
                }

                ReportUnindexedJoinColumn(first, firstTable);
                ReportUnindexedJoinColumn(second, secondTable);
            }
        }

        private void ReportUnindexedJoinColumn(ColumnReferenceExpression column, TableReference table)
        {
            var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
            if (IsIndexedColumn(table.Name, columnName))
            {
                return;
            }

            Add("unindexed-join", column, $"JOIN compares '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
        }

        private bool TryFindUnindexedTableColumn(
            ColumnReferenceExpression column,
            IReadOnlyList<TableReference> tables,
            out TableReference table)
        {
            table = null!;

            if (!TryFindTable(column, tables, out table))
            {
                return false;
            }

            var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;

            if (IsIndexedColumn(table.Name, columnName))
            {
                return false;
            }

            return true;
        }

        private bool IsIndexedColumn(string tableName, string columnName)
        {
            if (!indexedColumnsByTable.TryGetValue(tableName, out var indexedColumns))
            {
                return false;
            }

            return indexedColumns.Contains(columnName);
        }

        private bool TryResolveJoinOperands(
            BooleanComparisonExpression comparison,
            IReadOnlyList<TableReference> tables,
            out ColumnReferenceExpression first,
            out ColumnReferenceExpression second,
            out TableReference firstTable,
            out TableReference secondTable)
        {
            first = null!;
            second = null!;
            firstTable = null!;
            secondTable = null!;

            if (comparison.FirstExpression is not ColumnReferenceExpression firstColumn)
            {
                return false;
            }

            if (comparison.SecondExpression is not ColumnReferenceExpression secondColumn)
            {
                return false;
            }

            if (!TryFindTable(firstColumn, tables, out firstTable))
            {
                return false;
            }

            if (!TryFindTable(secondColumn, tables, out secondTable))
            {
                return false;
            }

            first = firstColumn;
            second = secondColumn;

            return true;
        }

        private HashSet<string> IndexedColumnsFor(string tableName, BooleanExpression where)
        {
            var columns = indexedColumnsByTable.TryGetValue(tableName, out var indexedColumns)
                ? indexedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!filteredIndexColumnsByTable.TryGetValue(tableName, out var filteredColumns))
            {
                return columns;
            }

            _scriptGenerator.GenerateScript(where, out var whereSql);
            var normalizedWhere = SqlExpressionNormalizer.Normalize(whereSql);

            foreach (var indexColumn in filteredColumns)
            {
                if (indexColumn.FilterPredicate is not { } predicate)
                {
                    continue;
                }

                if (!FilteredPredicateMatches(tableName, predicate, whereSql, normalizedWhere))
                {
                    continue;
                }

                columns.Add(indexColumn.ColumnName);
            }

            return columns;
        }

        private static bool TryFindTable(ColumnReferenceExpression column, IReadOnlyList<TableReference> tables, out TableReference table)
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;
            if (identifiers is null)
            {
                table = null!;
                return false;
            }

            var name = identifiers[^1].Value;
            var candidates = identifiers.Count == 2
                ? tables.Where(item => item.Matches(identifiers[0].Value) && item.Columns.Contains(name)).ToArray()
                : tables.Where(item => item.Columns.Contains(name)).ToArray();
            table = candidates.Length == 1 ? candidates[0] : null!;
            return table is not null;
        }
    }
}
