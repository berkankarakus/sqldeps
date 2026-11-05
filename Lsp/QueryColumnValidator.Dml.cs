using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(UpdateStatement node)
        {
            var specification = node.UpdateSpecification;
            var writtenTarget = specification.Target as NamedTableReference;

            if (writtenTarget is null)
            {
                return;
            }

            if (ReportAmbiguousWriteTarget(
                writtenTarget,
                specification.FromClause,
                "ambiguous-update-target",
                "UPDATE"))
            {
                return;
            }

            var target = ResolveWriteTarget(writtenTarget, specification.FromClause);

            if (target is null)
            {
                return;
            }

            ReportUnindexedDmlWhere(specification.WhereClause?.SearchCondition, DmlTables(target, specification.FromClause));

            if (!TryTableColumns(target, out var tableName, out var knownColumns))
            {
                return;
            }

            ValidateUpdateAssignments(specification, tableName, knownColumns);
            ReportDuplicateUpdateColumns(specification);
        }

        private void ValidateUpdateAssignments(UpdateSpecification specification, string tableName, IReadOnlySet<string> knownColumns)
        {
            foreach (var assignment in specification.SetClauses.OfType<AssignmentSetClause>())
            {
                var column = assignment.Column;
                var columnName = column?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;

                if (columnName is not null && !knownColumns.Contains(columnName))
                {
                    Add("unknown-update-column", column!, InvalidColumnMessage("UPDATE column", columnName, tableName, knownColumns));
                }
            }
        }

        private void ReportDuplicateUpdateColumns(UpdateSpecification specification)
        {
            foreach (var duplicates in specification.SetClauses
                .OfType<AssignmentSetClause>()
                .Where(assignment => assignment.Column?.MultiPartIdentifier?.Identifiers.Count > 0)
                .GroupBy(assignment => assignment.Column!.MultiPartIdentifier!.Identifiers[^1].Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var duplicate in duplicates.Skip(1))
                {
                    Add("duplicate-update-column", duplicate.Column!, $"Column '{duplicates.Key}' is assigned more than once in this UPDATE statement.");
                }
            }
        }

        public override void Visit(DeleteStatement node)
        {
            var specification = node.DeleteSpecification;
            var fromClause = specification.FromClause;
            var writtenTarget = specification.Target as NamedTableReference;

            if (fromClause is null || writtenTarget is null)
            {
                return;
            }

            if (ReportAmbiguousWriteTarget(
                writtenTarget,
                fromClause,
                "ambiguous-delete-target",
                "DELETE"))
            {
                return;
            }

            var target = ResolveWriteTarget(writtenTarget, fromClause);

            if (target is null)
            {
                AddUnboundDeleteTarget(writtenTarget);
                return;
            }

            ReportUnindexedDmlWhere(specification.WhereClause?.SearchCondition, DmlTables(target, fromClause));

            return;
        }

        private void AddUnboundDeleteTarget(NamedTableReference writtenTarget)
        {
            var name = writtenTarget.SchemaObject.BaseIdentifier.Value;
            Add("unbound-delete-target", writtenTarget, $"DELETE target '{name}' could not be bound to a table or alias in this query.");
        }

        private void ReportUnindexedDmlWhere(BooleanExpression? where, IReadOnlyList<TableReference> tables)
        {
            if (where is null)
            {
                return;
            }

            foreach (var column in ColumnReferenceFinder.Find(where))
            {
                var indexedTable = FindIndexedTable(column, tables);

                if (indexedTable is null)
                {
                    continue;
                }

                var (table, indexedColumns) = indexedTable.Value;
                var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;

                if (!indexedColumns.Contains(columnName))
                {
                    Add("unindexed-where", column, $"WHERE filters '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
                }
            }
        }

        private (TableReference Table, HashSet<string> Columns)? FindIndexedTable(
            ColumnReferenceExpression column,
            IReadOnlyList<TableReference> tables
        )
        {
            if (!TryFindTable(column, tables, out var table))
            {
                return null;
            }

            if (!indexedColumnsByTable.TryGetValue(table.Name, out var columns))
            {
                return null;
            }

            return (table, columns);
        }

        private IReadOnlyList<TableReference> DmlTables(NamedTableReference target, FromClause? fromClause)
        {
            return TableReferenceFinder.Find(fromClause)
                .Select(TryTableReference)
                .OfType<TableReference>()
                .Append(TryTableReference(target))
                .OfType<TableReference>()
                .Distinct()
                .ToArray();
        }

        private static NamedTableReference? ResolveWriteTarget(NamedTableReference target, FromClause? fromClause)
        {
            if (target.SchemaObject.SchemaIdentifier is not null)
            {
                return target;
            }

            var alias = target.SchemaObject.BaseIdentifier.Value;
            return MatchingWriteTargets(alias, fromClause).FirstOrDefault();
        }

        private static bool HasAmbiguousWriteTarget(NamedTableReference target, FromClause? fromClause)
        {
            if (target.SchemaObject.SchemaIdentifier is not null)
            {
                return false;
            }

            var alias = target.SchemaObject.BaseIdentifier.Value;
            return MatchingWriteTargets(alias, fromClause).Count() > 1;
        }

        private bool ReportAmbiguousWriteTarget(
            NamedTableReference target,
            FromClause? fromClause,
            string rule,
            string operation)
        {
            if (!HasAmbiguousWriteTarget(target, fromClause))
            {
                return false;
            }

            var name = target.SchemaObject.BaseIdentifier.Value;
            Add(rule, target, $"{operation} target '{name}' matches more than one table alias in this query.");

            return true;
        }

        private static IEnumerable<NamedTableReference> MatchingWriteTargets(string alias, FromClause? fromClause) => TableReferenceFinder.Find(fromClause)
            .Where(reference => string.Equals(reference.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase)
                || reference.Alias is null
                && string.Equals(reference.SchemaObject.BaseIdentifier.Value, alias, StringComparison.OrdinalIgnoreCase));
    }
}
