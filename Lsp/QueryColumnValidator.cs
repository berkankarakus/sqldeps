using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Finds column references that can be checked against the indexed table schema.</summary>
internal static class QueryColumnValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        IEnumerable<TableColumn> tableColumns,
        IEnumerable<LocalColumn> localColumns
    )
    {
        var columnsByTable = tableColumns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var columnsByLocalName = localColumns
            .GroupBy(column => column.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            ColumnVisitor visitor = new(sourceFile, batch.StartLine, columnsByTable, columnsByLocalName);
            batch.Script.Accept(visitor);
            findings.AddRange(LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine));
        }

        return findings;
    }

    private sealed class ColumnVisitor(
        string sourceFile,
        int batchStartLine,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        IReadOnlyDictionary<string, HashSet<string>> columnsByLocalName
    ) : TSqlFragmentVisitor
    {
        public List<LintFinding> Findings { get; } = [];

        public override void Visit(QuerySpecification node)
        {
            var nestedQueries = NestedQueries(node);
            var tables = TablesFor(node, nestedQueries);
            var orderColumns = node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<ColumnReferenceExpression>()
                .ToHashSet() ?? [];

            foreach (var column in ColumnReferenceFinder.Find(node)
                .Where(column => !IsNested(column, nestedQueries) && !orderColumns.Contains(column)))
            {
                ValidateColumn(column, tables);
            }

            var selectAliases = node.SelectElements
                .OfType<SelectScalarExpression>()
                .Select(element => element.ColumnName?.Value)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReportDistinctOrderBy(node, orderColumns, selectAliases);

            foreach (var ordinal in node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<IntegerLiteral>() ?? [])
            {
                if (int.TryParse(ordinal.Value, out var position)
                    && (position < 1 || position > node.SelectElements.Count))
                {
                    Add("order-by-ordinal", ordinal, $"ORDER BY position {position} is outside the SELECT list (1 to {node.SelectElements.Count}).");
                }
            }

            foreach (var column in orderColumns)
            {
                var identifiers = column.MultiPartIdentifier?.Identifiers;
                if (identifiers?.Count == 1 && selectAliases.Contains(identifiers[0].Value))
                {
                    continue;
                }

                ValidateColumn(column, tables);
            }
        }

        private void ReportDistinctOrderBy(
            QuerySpecification query,
            IEnumerable<ColumnReferenceExpression> orderColumns,
            HashSet<string> selectAliases
        )
        {
            if (query.UniqueRowFilter != UniqueRowFilter.Distinct)
            {
                return;
            }

            var selectColumnNames = query.SelectElements
                .OfType<SelectScalarExpression>()
                .Select(element => element.Expression as ColumnReferenceExpression)
                .Where(column => column?.MultiPartIdentifier?.Identifiers.Count > 0)
                .Select(column => column!.MultiPartIdentifier!.Identifiers[^1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in orderColumns)
            {
                var identifiers = column.MultiPartIdentifier?.Identifiers;
                if (identifiers?.Count != 1
                    || selectAliases.Contains(identifiers[0].Value)
                    || selectColumnNames.Contains(identifiers[0].Value))
                {
                    continue;
                }

                Add("distinct-order-by", column, $"ORDER BY column '{identifiers[0].Value}' must appear in the SELECT list when DISTINCT is used.");
            }
        }

        public override void Visit(InsertStatement node)
        {
            var target = node.InsertSpecification.Target as NamedTableReference;

            if (target is null)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out var knownColumns))
            {
                return;
            }

            foreach (var column in node.InsertSpecification.Columns)
            {
                var columnName = column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (columnName is not null && !knownColumns.Contains(columnName))
                {
                    Add("unknown-insert-column", column, InvalidColumnMessage("INSERT column", columnName, tableName, knownColumns));
                }
            }

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

            if (node.InsertSpecification.InsertSource is ValuesInsertSource values
                && node.InsertSpecification.Columns.Count > 0)
            {
                foreach (var row in values.RowValues.Where(row => row.ColumnValues.Count != node.InsertSpecification.Columns.Count))
                {
                    Add("insert-value-count", row, $"INSERT lists {node.InsertSpecification.Columns.Count} target columns but supplies {row.ColumnValues.Count} values.");
                }
            }

            if (node.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification query }
                && node.InsertSpecification.Columns.Count != query.SelectElements.Count)
            {
                Add("insert-select-count", query, $"INSERT lists {node.InsertSpecification.Columns.Count} target columns but SELECT returns {query.SelectElements.Count} expressions.");
            }
        }

        public override void Visit(UpdateStatement node)
        {
            var specification = node.UpdateSpecification;
            var writtenTarget = specification.Target as NamedTableReference;

            if (writtenTarget is null)
            {
                return;
            }

            var target = ResolveWriteTarget(writtenTarget, specification.FromClause);

            if (target is null)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out var knownColumns))
            {
                return;
            }

            foreach (var assignment in specification.SetClauses.OfType<AssignmentSetClause>())
            {
                var column = assignment.Column;
                var columnName = column?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (columnName is not null && !knownColumns.Contains(columnName))
                {
                    Add("unknown-update-column", column!, InvalidColumnMessage("UPDATE column", columnName, tableName, knownColumns));
                }
            }

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

            if (fromClause is null)
            {
                return;
            }

            if (writtenTarget is null)
            {
                return;
            }

            var target = ResolveWriteTarget(writtenTarget, fromClause);

            if (target is null)
            {
                AddUnboundDeleteTarget(writtenTarget);
                return;
            }

            if (TryTableColumns(target, out _, out _))
            {
                return;
            }

            AddUnboundDeleteTarget(writtenTarget);
        }

        private void AddUnboundDeleteTarget(NamedTableReference writtenTarget)
        {
            var name = writtenTarget.SchemaObject.BaseIdentifier.Value;
            Add("unbound-delete-target", writtenTarget, $"DELETE target '{name}' could not be bound to a table or alias in this query.");
        }

        private static NamedTableReference? ResolveWriteTarget(NamedTableReference target, FromClause? fromClause)
        {
            if (target.SchemaObject.SchemaIdentifier is not null)
            {
                return target;
            }

            var alias = target.SchemaObject.BaseIdentifier.Value;
            return TableReferenceFinder.Find(fromClause).FirstOrDefault(reference =>
                string.Equals(reference.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase)) ?? target;
        }

        private static bool IsNested(TSqlFragment fragment, IEnumerable<QuerySpecification> nestedQueries) => nestedQueries.Any(query =>
            fragment.StartOffset >= query.StartOffset
            && fragment.StartOffset < query.StartOffset + query.FragmentLength);

        private static IReadOnlyList<QuerySpecification> NestedQueries(QuerySpecification node) => QuerySpecificationFinder.Find(node)
            .Where(query => !ReferenceEquals(query, node))
            .ToArray();

        private IReadOnlyList<TableReference> TablesFor(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries) => TableReferenceFinder.Find(node.FromClause)
            .Where(table => !IsNested(table, nestedQueries))
            .Select(TryTableReference)
            .OfType<TableReference>()
            .Concat(QueryDerivedTableFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries))
                .Select(TryDerivedTableReference)
                .OfType<TableReference>())
            .ToArray();

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
            if (columnsByTable.TryGetValue(tableName, out var columns))
            {
                return new TableReference(reference.Alias?.Value, tableName, columns);
            }

            var localName = reference.SchemaObject.BaseIdentifier.Value;
            return columnsByLocalName.TryGetValue(localName, out columns)
                ? new TableReference(reference.Alias?.Value, localName, columns)
                : null;
        }

        private TableReference? TryDerivedTableReference(QueryDerivedTable reference)
        {
            var alias = reference.Alias?.Value;

            if (alias is null)
            {
                return null;
            }

            if (!columnsByLocalName.TryGetValue(alias, out var columns))
            {
                return null;
            }

            return new TableReference(alias, alias, columns);
        }

        private void Add(string rule, TSqlFragment node, string message)
        {
            Findings.Add(new LintFinding(
                rule,
                "error",
                "(batch)",
                sourceFile,
                batchStartLine + node.StartLine - 1,
                node.StartColumn,
                node.FragmentLength,
                message));
        }
    }

    private sealed record TableReference(string? Alias, string Name, IReadOnlySet<string> Columns)
    {
        public string Qualifier => Alias ?? Name.Split('.').Last();

        public bool Matches(string qualifier)
        {
            if (string.Equals(Alias, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Alias is not null)
            {
                return false;
            }

            return string.Equals(Name.Split('.').Last(), qualifier, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ColumnReferenceFinder : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> References { get; } = [];

        public static IEnumerable<ColumnReferenceExpression> Find(TSqlFragment fragment)
        {
            ColumnReferenceFinder finder = new();
            fragment.Accept(finder);
            return finder.References;
        }

        public override void Visit(ColumnReferenceExpression node) => References.Add(node);
    }

    private sealed class QuerySpecificationFinder : TSqlFragmentVisitor
    {
        public List<QuerySpecification> Queries { get; } = [];

        public static IEnumerable<QuerySpecification> Find(TSqlFragment fragment)
        {
            QuerySpecificationFinder finder = new();
            fragment.Accept(finder);
            return finder.Queries;
        }

        public override void Visit(QuerySpecification node) => Queries.Add(node);
    }

    private sealed class QueryDerivedTableFinder : TSqlFragmentVisitor
    {
        public List<QueryDerivedTable> Tables { get; } = [];

        public static IEnumerable<QueryDerivedTable> Find(TSqlFragment? fragment)
        {
            QueryDerivedTableFinder finder = new();
            fragment?.Accept(finder);
            return finder.Tables;
        }

        public override void Visit(QueryDerivedTable node) => Tables.Add(node);
    }
}
