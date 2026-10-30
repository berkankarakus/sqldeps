using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Globalization;
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
        IEnumerable<ResultColumn> resultColumns,
        IEnumerable<TableKey> tableKeys,
        IEnumerable<TableIndexColumn> indexColumns,
        IEnumerable<TableForeignKey> foreignKeys,
        IEnumerable<LocalColumn> localColumns
    )
    {
        var indexedColumns = tableColumns.ToArray();
        var columnsByTable = indexedColumns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var metadataByTable = indexedColumns
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var tablesByBaseName = indexedColumns
            .Select(column => column.TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(name => name.Split('.').Last(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            var batchEndLine = batch.StartLine + batch.Text.Count(character => character == '\n');
            var columnsByLocalName = localColumns
                .Where(column => column.Line >= batch.StartLine && column.Line <= batchEndLine)
                .GroupBy(column => column.Alias, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        var resultColumnsByObject = resultColumns
            .GroupBy(column => column.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(column => column.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
        var indexedColumnsByTable = indexColumns
            .Where(column => !column.IsIncluded && !column.IsFiltered)
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var filteredIndexColumnsByTable = indexColumns
            .Where(column => !column.IsIncluded && column.IsFiltered && column.FilterPredicate is not null)
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in tableKeys)
        {
            indexedColumnsByTable.GetValueOrDefault(key.TableName)?.Add(key.ColumnName);
        }
        var foreignKeysByColumn = foreignKeys
            .GroupBy(key => (key.TableName, key.ColumnName))
            .ToDictionary(group => group.Key, group => group.First());
            var queries = QuerySpecificationFinder.Find(batch.Script).ToArray();
            var cteNames = CteNameCollector.Collect(batch.Script);
            ColumnVisitor visitor = new(sourceFile, batch.StartLine, columnsByTable, metadataByTable, resultColumnsByObject, indexedColumnsByTable, filteredIndexColumnsByTable, foreignKeysByColumn, columnsByLocalName, tablesByBaseName, queries, cteNames);
            batch.Script.Accept(visitor);
            findings.AddRange(LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine));
        }

        return findings;
    }

    private sealed class ColumnVisitor(
        string sourceFile,
        int batchStartLine,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        IReadOnlyDictionary<string, TableColumn[]> metadataByTable,
        IReadOnlyDictionary<string, ResultColumn[]> resultColumnsByObject,
        IReadOnlyDictionary<string, HashSet<string>> indexedColumnsByTable,
        IReadOnlyDictionary<string, TableIndexColumn[]> filteredIndexColumnsByTable,
        IReadOnlyDictionary<(string TableName, string ColumnName), TableForeignKey> foreignKeysByColumn,
        IReadOnlyDictionary<string, LocalColumn[]> columnsByLocalName,
        IReadOnlyDictionary<string, string[]> tablesByBaseName,
        IReadOnlyList<QuerySpecification> allQueries,
        IReadOnlySet<string> cteNames
    ) : TSqlFragmentVisitor
    {
        private int _scopeLine;
        private readonly Sql160ScriptGenerator _scriptGenerator = new();

        public List<LintFinding> Findings { get; } = [];

        public override void Visit(ProcedureStatementBody node)
        {
            var previousScope = _scopeLine;
            _scopeLine = LineOf(node);

            try
            {
                base.Visit(node);
            }
            finally
            {
                _scopeLine = previousScope;
            }
        }

        public override void Visit(QuerySpecification node)
        {
            var nestedQueries = NestedQueries(node);
            var tables = TablesFor(node, nestedQueries);
            ReportUnindexedWhere(node, tables);
            ReportUnindexedOrderBy(node, tables);
            ReportUnindexedJoins(node, tables);
            ReportCteAliasShadowing(tables);
            ReportUnknownTables(node, nestedQueries);
            var orderColumns = node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<ColumnReferenceExpression>()
                .ToHashSet() ?? [];

            ValidateQueryColumns(node, nestedQueries, orderColumns, tables);

            var selectAliases = node.SelectElements
                .OfType<SelectScalarExpression>()
                .Select(element => element.ColumnName?.Value)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReportDistinctOrderBy(node, orderColumns, selectAliases);
            ReportOrderByOrdinals(node);
            ValidateOrderByColumns(orderColumns, selectAliases, tables);
        }

        private void ReportUnindexedWhere(QuerySpecification query, IReadOnlyList<TableReference> tables)
        {
            if (query.WhereClause is null)
            {
                return;
            }

            foreach (var column in ColumnReferenceFinder.Find(query.WhereClause))
            {
                if (!TryFindTable(column, tables, out var table))
                {
                    continue;
                }

                var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
                if (IndexedColumnsFor(table.Name, query.WhereClause.SearchCondition).Contains(columnName))
                {
                    continue;
                }

                Add("unindexed-where", column, $"WHERE filters '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
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
                if (!TryFindTable(column, tables, out var table)
                    || !indexedColumnsByTable.TryGetValue(table.Name, out var indexedColumns)
                    || indexedColumns.Contains(column.MultiPartIdentifier!.Identifiers[^1].Value))
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
                if (comparison.FirstExpression is not ColumnReferenceExpression first
                    || comparison.SecondExpression is not ColumnReferenceExpression second
                    || !TryFindTable(first, tables, out var firstTable)
                    || !TryFindTable(second, tables, out var secondTable))
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
            if (!indexedColumnsByTable.TryGetValue(table.Name, out var indexedColumns)
                || indexedColumns.Contains(columnName))
            {
                return;
            }

            Add("unindexed-join", column, $"JOIN compares '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
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
            var normalizedWhere = NormalizeSql(whereSql);

            foreach (var indexColumn in filteredColumns)
            {
                if (indexColumn.FilterPredicate is not { } predicate
                    || !normalizedWhere.Contains(NormalizeSql(predicate), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                columns.Add(indexColumn.ColumnName);
            }

            return columns;
        }

        private static string NormalizeSql(string sql) => string.Concat(sql
            .Where(character => !char.IsWhiteSpace(character)))
            .Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .ToUpperInvariant();

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

        public override void Visit(BooleanComparisonExpression node)
        {
            ReportImplicitFilterConversion(node);
            ReportForeignKeyJoinMismatch(node);
            ReportJoinTypeMismatch(node);
            base.Visit(node);
        }

        /// <summary>Reports only deterministic type-family or collation mismatches in JOIN equality predicates.</summary>
        private void ReportJoinTypeMismatch(BooleanComparisonExpression comparison)
        {
            if (comparison.ComparisonType != BooleanComparisonType.Equals
                || comparison.FirstExpression is not ColumnReferenceExpression first
                || comparison.SecondExpression is not ColumnReferenceExpression second)
            {
                return;
            }

            var query = allQueries
                .Where(candidate => Contains(candidate, comparison))
                .OrderBy(candidate => candidate.FragmentLength)
                .FirstOrDefault();

            if (query is null)
            {
                return;
            }

            var joinComparisons = new JoinComparisonVisitor();
            query.FromClause?.Accept(joinComparisons);

            if (!joinComparisons.Comparisons.Contains(comparison))
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryGetColumnMetadata(first, tables, out var firstMetadata)
                || !TryGetColumnMetadata(second, tables, out var secondMetadata)
                || !AreIncompatible(firstMetadata, secondMetadata))
            {
                return;
            }

            Add("join-type-mismatch", first, $"JOIN compares '{firstMetadata.DataType}' column '{firstMetadata.ColumnName}' with '{secondMetadata.DataType}' column '{secondMetadata.ColumnName}'.", "info");
        }

        /// <summary>Determines whether metadata differs in a way that can cause a lossy JOIN conversion.</summary>
        private static bool AreIncompatible(TableColumn first, TableColumn second)
        {
            var firstFamily = TypeFamily(first.DataType);
            var secondFamily = TypeFamily(second.DataType);

            if (firstFamily != secondFamily && firstFamily != "other" && secondFamily != "other")
            {
                return true;
            }

            return firstFamily == "string"
                && secondFamily == "string"
                && first.Collation is not null
                && second.Collation is not null
                && !string.Equals(first.Collation, second.Collation, StringComparison.OrdinalIgnoreCase);
        }

        private static string TypeFamily(string dataType)
        {
            if (IsNumericType(dataType))
            {
                return "numeric";
            }

            if (dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase))
            {
                return "datetime";
            }

            if (dataType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                return "string";
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return "guid";
            }

            return "other";
        }

        private void ReportForeignKeyJoinMismatch(BooleanComparisonExpression comparison)
        {
            if (comparison.ComparisonType != BooleanComparisonType.Equals
                || comparison.FirstExpression is not ColumnReferenceExpression first
                || comparison.SecondExpression is not ColumnReferenceExpression second)
            {
                return;
            }

            var query = allQueries.Where(candidate => Contains(candidate, comparison)).OrderBy(candidate => candidate.FragmentLength).FirstOrDefault();
            if (query is null)
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryFindTable(first, tables, out var firstTable)
                || !TryFindTable(second, tables, out var secondTable))
            {
                return;
            }

            ReportForeignKeySide(first, firstTable, secondTable);
            ReportForeignKeySide(second, secondTable, firstTable);
        }

        private void ReportForeignKeySide(ColumnReferenceExpression column, TableReference table, TableReference otherTable)
        {
            var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
            if (!foreignKeysByColumn.TryGetValue((table.Name, columnName), out var foreignKey))
            {
                return;
            }

            if (string.Equals(foreignKey.ReferencedTableName, otherTable.Name, StringComparison.OrdinalIgnoreCase)
                && otherTable.Columns.Contains(foreignKey.ReferencedColumnName))
            {
                return;
            }

            Add("foreign-key-join-mismatch", column, $"Foreign key '{table.Name}.{columnName}' references '{foreignKey.ReferencedTableName}.{foreignKey.ReferencedColumnName}', but this JOIN uses '{otherTable.Name}'.", "info");
        }

        private void ReportImplicitFilterConversion(BooleanComparisonExpression node)
        {
            if (!TryGetColumnAndLiteral(node, out var column, out var literal))
            {
                return;
            }

            var query = allQueries
                .Where(candidate => Contains(candidate, node))
                .OrderBy(candidate => candidate.FragmentLength)
                .FirstOrDefault();

            if (query is null)
            {
                return;
            }

            var tables = TablesFor(query, NestedQueries(query));
            if (!TryGetColumnMetadata(column, tables, out var metadata)
                || !IsStringConversionTarget(metadata.DataType))
            {
                return;
            }

            Add(
                "implicit-filter-conversion",
                literal,
                $"String literal '{literal.Value}' is compared with '{metadata.DataType}' column '{column.MultiPartIdentifier!.Identifiers[^1].Value}' and may require an implicit conversion.",
                "info");
        }

        private static bool TryGetColumnAndLiteral(
            BooleanComparisonExpression comparison,
            out ColumnReferenceExpression column,
            out StringLiteral literal)
        {
            if (comparison.FirstExpression is ColumnReferenceExpression firstColumn
                && comparison.SecondExpression is StringLiteral secondLiteral)
            {
                column = firstColumn;
                literal = secondLiteral;

                return true;
            }

            if (comparison.SecondExpression is ColumnReferenceExpression secondColumn
                && comparison.FirstExpression is StringLiteral firstLiteral)
            {
                column = secondColumn;
                literal = firstLiteral;

                return true;
            }

            column = null!;
            literal = null!;

            return false;
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

                Add("unknown-table", table, $"Table or view '{tableName}' was not found in the workspace index.", "warning");
            }
        }

        private bool IsKnownTable(NamedTableReference table, string tableName, string localName)
        {
            return columnsByTable.ContainsKey(tableName)
                || HasLocalColumns(localName, LineOf(table))
                || cteNames.Contains(localName)
                || ObjectNames.IsTemporary(table.SchemaObject);
        }

        private void ValidateQueryColumns(
            QuerySpecification node,
            IReadOnlyList<QuerySpecification> nestedQueries,
            IReadOnlySet<ColumnReferenceExpression> orderColumns,
            IReadOnlyList<TableReference> tables
        )
        {
            foreach (var column in ColumnReferenceFinder.Find(node)
                .Where(column => !IsNested(column, nestedQueries) && !orderColumns.Contains(column)))
            {
                ValidateColumn(column, tables);
            }
        }

        private void ReportOrderByOrdinals(QuerySpecification node)
        {
            foreach (var ordinal in node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<IntegerLiteral>() ?? [])
            {
                if (!int.TryParse(ordinal.Value, out var position))
                {
                    continue;
                }

                if (position >= 1 && position <= node.SelectElements.Count)
                {
                    continue;
                }

                Add("order-by-ordinal", ordinal, $"ORDER BY position {position} is outside the SELECT list (1 to {node.SelectElements.Count}).");
            }
        }

        private void ValidateOrderByColumns(
            IEnumerable<ColumnReferenceExpression> orderColumns,
            IReadOnlySet<string> selectAliases,
            IReadOnlyList<TableReference> tables
        )
        {
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
                if (!ShouldReportDistinctOrderByColumn(column, selectAliases, selectColumnNames))
                {
                    continue;
                }

                var columnName = column.MultiPartIdentifier!.Identifiers[0].Value;
                Add("distinct-order-by", column, $"ORDER BY column '{columnName}' must appear in the SELECT list when DISTINCT is used.");
            }
        }

        private static bool ShouldReportDistinctOrderByColumn(
            ColumnReferenceExpression column,
            IReadOnlySet<string> selectAliases,
            IReadOnlySet<string> selectColumnNames
        )
        {
            var identifiers = column.MultiPartIdentifier?.Identifiers;

            if (identifiers?.Count != 1)
            {
                return false;
            }

            var columnName = identifiers[0].Value;

            if (selectAliases.Contains(columnName))
            {
                return false;
            }

            return !selectColumnNames.Contains(columnName);
        }

        private void ReportCteAliasShadowing(IReadOnlyList<TableReference> tables)
        {
            foreach (var table in tables.Where(table => table.Alias is not null && cteNames.Contains(table.Alias)))
            {
                Add("cte-alias-shadowing", table.Node, $"Table alias '{table.Alias}' shadows a CTE with the same name in this query scope.");
            }
        }

        public override void Visit(InsertStatement node)
        {
            ReportInsertExecuteCount(node);

            var target = node.InsertSpecification.Target as NamedTableReference;

            if (target is null)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out var knownColumns))
            {
                return;
            }

            ValidateInsertColumns(node, tableName, knownColumns);
            ReportMissingRequiredInsertColumns(node, tableName);
            ReportIdentityInsert(node, tableName);
            ReportDuplicateInsertColumns(node);
            ReportInsertValues(node);
            ReportInsertLiteralTypes(node, tableName);
            ReportInsertSelectCount(node);
            ReportInsertSelectNullability(node, metadataByTable.GetValueOrDefault(tableName) ?? []);
        }

        /// <summary>Reports NULL expressions assigned to known non-nullable INSERT SELECT targets.</summary>
        private void ReportInsertSelectNullability(InsertStatement node, IReadOnlyCollection<TableColumn> knownColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0
                || node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            var columnsByName = knownColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            var tables = TablesFor(query, NestedQueries(query));
            foreach (var pair in node.InsertSpecification.Columns.Zip(query.SelectElements))
            {
                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null
                    || !columnsByName.TryGetValue(targetName, out var targetColumn)
                    || targetColumn.IsNullable != false
                    || pair.Second is not SelectScalarExpression scalar)
                {
                    continue;
                }

                if (scalar.Expression is NullLiteral nullLiteral)
                {
                    Add("nullability-violation", nullLiteral, $"NULL is assigned to non-nullable column '{targetName}'.", "warning");
                    continue;
                }

                if (scalar.Expression is ColumnReferenceExpression sourceColumn
                    && TryGetColumnMetadata(sourceColumn, tables, out var sourceMetadata)
                    && sourceMetadata.IsNullable == true)
                {
                    Add("nullable-source-insert", sourceColumn, $"Nullable source column '{sourceMetadata.ColumnName}' is inserted into non-nullable column '{targetName}'.", "info");
                }
            }
        }

        private void ReportInsertExecuteCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not ExecuteInsertSource { Execute: var execute }
                || execute.ExecutableEntity is not ExecutableProcedureReference procedure
                || procedure.ProcedureReference?.ProcedureReference?.Name is not { } name)
            {
                return;
            }

            var objectName = ObjectNames.Format(name, ObjectNames.StandardDefaultSchema);
            if (!resultColumnsByObject.TryGetValue(objectName, out var resultColumns)
                || !TryInsertTargetColumnCount(node.InsertSpecification, out var targetColumnCount))
            {
                return;
            }

            if (targetColumnCount != resultColumns.Length)
            {
                Add("insert-exec-count", node, $"INSERT targets {targetColumnCount} column(s), but procedure '{objectName}' returns {resultColumns.Length}.", "warning");
            }

            ReportInsertExecuteTypes(node, resultColumns);
        }

        /// <summary>Reports result-set type-family differences only when both sides are explicit.</summary>
        private void ReportInsertExecuteTypes(InsertStatement node, IReadOnlyList<ResultColumn> resultColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0
                || node.InsertSpecification.Target is not NamedTableReference target
                || !TryTableColumns(target, out var tableName, out _)
                || !metadataByTable.TryGetValue(tableName, out var targetColumns))
            {
                return;
            }

            var targetByName = targetColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in node.InsertSpecification.Columns.Zip(resultColumns))
            {
                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null
                    || !targetByName.TryGetValue(targetName, out var targetColumn)
                    || pair.Second.DataType is not { } resultType
                    || !TypeFamiliesDiffer(targetColumn.DataType, resultType))
                {
                    continue;
                }

                Add("insert-exec-type", pair.First, $"Procedure result column '{pair.Second.ColumnName}' has type '{resultType}', but target column '{targetName}' has type '{targetColumn.DataType}'.", "info");
            }
        }

        private static bool TypeFamiliesDiffer(string first, string second)
        {
            var firstFamily = TypeFamily(first);
            var secondFamily = TypeFamily(second);

            return firstFamily != "other"
                && secondFamily != "other"
                && !string.Equals(firstFamily, secondFamily, StringComparison.Ordinal);
        }

        private bool TryInsertTargetColumnCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference namedTarget
                && TryTableColumns(namedTarget, out _, out var knownColumns))
            {
                count = knownColumns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference localTarget)
            {
                count = LocalColumnsFor(localTarget.SchemaObject.BaseIdentifier.Value, LineOf(localTarget)).Count;

                if (count > 0)
                {
                    return true;
                }
            }

            if (specification.Target is VariableTableReference variableTarget)
            {
                count = LocalColumnsFor(variableTarget.Variable.Name, LineOf(variableTarget)).Count;

                return count > 0;
            }

            count = 0;

            return false;
        }

        private void ReportMissingRequiredInsertColumns(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns)
                || node.InsertSpecification.Columns.Count == 0)
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
            if (node.InsertSpecification.InsertSource is not ValuesInsertSource values
                || node.InsertSpecification.Columns.Count == 0)
            {
                return;
            }

            foreach (var row in values.RowValues.Where(row => row.ColumnValues.Count != node.InsertSpecification.Columns.Count))
            {
                Add("insert-value-count", row, $"INSERT lists {node.InsertSpecification.Columns.Count} target columns but supplies {row.ColumnValues.Count} values.");
            }
        }

        private void ReportInsertLiteralTypes(InsertStatement node, string tableName)
        {
            if (!metadataByTable.TryGetValue(tableName, out var columns)
                || node.InsertSpecification.InsertSource is not ValuesInsertSource values)
            {
                return;
            }

            var metadata = columns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            foreach (var row in values.RowValues)
            {
                foreach (var pair in node.InsertSpecification.Columns.Zip(row.ColumnValues))
                {
                    var name = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                    if (name is null || !metadata.TryGetValue(name, out var column))
                    {
                        continue;
                    }

                    if (pair.Second is StringLiteral literal && IsStringConversionTarget(column.DataType))
                    {
                        Add("implicit-conversion", pair.Second, $"String literal '{literal.Value}' is assigned to '{column.DataType}' column '{name}' and may require an implicit conversion.", "warning");

                        if (HasDateTimePrecisionLoss(literal.Value, column))
                        {
                            Add("precision-loss", pair.Second, $"Date/time literal may lose precision when assigned to '{column.DataType}' column '{name}'.", "warning");
                        }
                    }

                    if (pair.Second is NullLiteral && column.IsNullable == false)
                    {
                        Add("nullability-violation", pair.Second, $"NULL is assigned to non-nullable column '{name}'.", "warning");
                    }

                    if (pair.Second is NumericLiteral or MoneyLiteral
                        && HasPrecisionLoss(pair.Second, column))
                    {
                        Add("precision-loss", pair.Second, $"Numeric literal may lose precision when assigned to '{column.DataType}' column '{name}'.", "warning");
                    }
                }
            }
        }

        private static bool HasPrecisionLoss(ScalarExpression expression, TableColumn column)
        {
            var value = expression switch
            {
                NumericLiteral numeric => numeric.Value,
                MoneyLiteral money => money.Value,
                _ => "",
            };

            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (IsIntegerType(column.DataType))
            {
                return number != decimal.Truncate(number);
            }

            if (IsMoneyType(column.DataType))
            {
                return DecimalScale(number) > 4;
            }

            if (IsFloatType(column.DataType))
            {
                var maximumDigits = column.DataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase) ? 7 : 15;
                return SignificantDigits(value) > maximumDigits;
            }

            return column.Scale is { } scale && DecimalScale(number) > scale;
        }

        private static bool HasDateTimePrecisionLoss(string value, TableColumn column)
        {
            var type = column.DataType;

            if (type.Equals("DATE", StringComparison.OrdinalIgnoreCase))
            {
                return value.Contains('T') || value.Contains(' ');
            }

            if (type.Equals("SMALLDATETIME", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && (date.Second != 0 || date.Millisecond != 0);
            }

            if (!IsDateTimeType(type))
            {
                return false;
            }

            var decimalSeparator = value.IndexOf('.');
            if (decimalSeparator < 0)
            {
                return false;
            }

            var fraction = value[(decimalSeparator + 1)..];
            var fractionLength = fraction.TakeWhile(char.IsDigit).Count();
            var allowedScale = column.Scale ?? (type.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("DATETIME2", StringComparison.OrdinalIgnoreCase) ? 3 : 7);

            return fractionLength > allowedScale;
        }

        private static bool IsDateTimeType(string dataType) => dataType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DATETIMEOFFSET", StringComparison.OrdinalIgnoreCase);

        private static int SignificantDigits(string value)
        {
            var digits = value.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            return digits.TrimEnd('0').Length;
        }

        private static int DecimalScale(decimal value)
        {
            value = decimal.Abs(value);
            var bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0x7F;
        }

        private static bool IsIntegerType(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase);

        private static bool IsMoneyType(string dataType) => dataType.StartsWith("MONEY", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLMONEY", StringComparison.OrdinalIgnoreCase);

        private static bool IsFloatType(string dataType) => dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);

        private static bool IsStringConversionTarget(string dataType) => IsNumericType(dataType)
            || dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase);

        private static bool IsNumericType(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);

        private void ReportInsertSelectCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query }
                || !TrySelectColumnCount(query, out var selectedColumnCount)
                || !TryInsertTargetCount(node.InsertSpecification, out var targetColumnCount)
                || targetColumnCount == selectedColumnCount)
            {
                return;
            }

            var targetDescription = node.InsertSpecification.Columns.Count > 0
                ? $"lists {targetColumnCount} target columns"
                : $"targets {targetColumnCount} columns";

            Add("insert-select-count", query, $"INSERT {targetDescription} but SELECT returns {selectedColumnCount} expressions.");
        }

        private bool TryInsertTargetCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (specification.Target is NamedTableReference target
                && TryTableColumns(target, out _, out var columns))
            {
                count = columns.Count;

                return true;
            }

            count = 0;

            return false;
        }

        private bool TrySelectColumnCount(QuerySpecification query, out int count)
        {
            var tables = TablesFor(query, NestedQueries(query));
            count = 0;

            foreach (var element in query.SelectElements)
            {
                if (element is SelectScalarExpression)
                {
                    count++;
                    continue;
                }

                if (element is not SelectStarExpression star)
                {
                    count = 0;

                    return false;
                }

                var qualifier = star.Qualifier?.Identifiers.LastOrDefault()?.Value;
                var sources = qualifier is null ? tables : tables.Where(table => table.Matches(qualifier));
                var sourceList = sources.ToArray();

                if (sourceList.Length == 0)
                {
                    count = 0;

                    return false;
                }

                count += sourceList.Sum(table => table.Columns.Count);
            }

            return true;
        }

        public override void Visit(UpdateStatement node)
        {
            var specification = node.UpdateSpecification;
            var writtenTarget = specification.Target as NamedTableReference;

            if (writtenTarget is null)
            {
                return;
            }

            if (HasAmbiguousWriteTarget(writtenTarget, specification.FromClause))
            {
                Add("ambiguous-update-target", writtenTarget, $"UPDATE target '{writtenTarget.SchemaObject.BaseIdentifier.Value}' matches more than one table alias in this query.");
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

        private void ValidateUpdateAssignments(
            UpdateSpecification specification,
            string tableName,
            IReadOnlySet<string> knownColumns
        )
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

            if (fromClause is null)
            {
                return;
            }

            if (writtenTarget is null)
            {
                return;
            }

            if (HasAmbiguousWriteTarget(writtenTarget, fromClause))
            {
                Add("ambiguous-delete-target", writtenTarget, $"DELETE target '{writtenTarget.SchemaObject.BaseIdentifier.Value}' matches more than one table alias in this query.");
                return;
            }

            var target = ResolveWriteTarget(writtenTarget, fromClause);

            if (target is null)
            {
                AddUnboundDeleteTarget(writtenTarget);
                return;
            }

            ReportUnindexedDmlWhere(specification.WhereClause?.SearchCondition, DmlTables(target, fromClause));

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

        private void ReportUnindexedDmlWhere(BooleanExpression? where, IReadOnlyList<TableReference> tables)
        {
            if (where is null)
            {
                return;
            }

            foreach (var column in ColumnReferenceFinder.Find(where))
            {
                if (!TryFindTable(column, tables, out var table)
                    || !indexedColumnsByTable.TryGetValue(table.Name, out var indexedColumns))
                {
                    continue;
                }

                var columnName = column.MultiPartIdentifier!.Identifiers[^1].Value;
                if (!indexedColumns.Contains(columnName))
                {
                    Add("unindexed-where", column, $"WHERE filters '{table.Name}.{columnName}', but no matching key/index column is known.", "info");
                }
            }
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
            return MatchingWriteTargets(alias, fromClause).FirstOrDefault() ?? target;
        }

        private static bool HasAmbiguousWriteTarget(NamedTableReference target, FromClause? fromClause)
        {
            if (target.SchemaObject.SchemaIdentifier is not null)
            {
                return false;
            }

            var alias = target.SchemaObject.BaseIdentifier.Value;
            var matches = MatchingWriteTargets(alias, fromClause).Count();

            return matches > 1;
        }

        private static IEnumerable<NamedTableReference> MatchingWriteTargets(string alias, FromClause? fromClause) => TableReferenceFinder.Find(fromClause)
            .Where(reference => string.Equals(reference.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase));

        private static bool IsNested(TSqlFragment fragment, IEnumerable<QuerySpecification> nestedQueries) => nestedQueries.Any(query =>
            fragment.StartOffset >= query.StartOffset
            && fragment.StartOffset < query.StartOffset + query.FragmentLength);

        private IReadOnlyList<QuerySpecification> NestedQueries(QuerySpecification node) => QuerySpecificationFinder.Find(node)
            .Where(query => !ReferenceEquals(query, node))
            .ToArray();

        private IReadOnlyList<TableReference> TablesFor(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries)
        {
            var directTables = DirectTables(node, nestedQueries);
            var outerTables = allQueries
                .Where(query => !ReferenceEquals(query, node) && Contains(query, node))
                .SelectMany(query => DirectTables(query, QuerySpecificationFinder.Find(query)
                    .Where(nested => !ReferenceEquals(nested, query)).ToArray()));

            return directTables.Concat(outerTables).Distinct().ToArray();
        }

        private IReadOnlyList<TableReference> DirectTables(QuerySpecification node, IReadOnlyList<QuerySpecification> nestedQueries) => TableReferenceFinder.Find(node.FromClause)
            .Where(table => !IsNested(table, nestedQueries))
            .Select(TryTableReference)
            .OfType<TableReference>()
            .Concat(VariableTableReferenceFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries))
                .Select(TryVariableTableReference)
                .OfType<TableReference>())
            .Concat(QueryDerivedTableFinder.Find(node.FromClause)
                .Where(table => !IsNested(table, nestedQueries))
                .Select(TryDerivedTableReference)
                .OfType<TableReference>())
            .ToArray();

        private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
            inner.StartOffset >= outer.StartOffset
            && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;

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

        private int LineOf(TSqlFragment node)
        {
            return batchStartLine + node.StartLine - 1;
        }

        private void Add(string rule, TSqlFragment node, string message, string severity = "error")
        {
            Findings.Add(new LintFinding(
                rule,
                severity,
                "(batch)",
                sourceFile,
                batchStartLine + node.StartLine - 1,
                node.StartColumn,
                node.FragmentLength,
                message));
        }
    }

    private sealed class JoinComparisonVisitor : TSqlFragmentVisitor
    {
        public List<BooleanComparisonExpression> Comparisons { get; } = [];

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType == BooleanComparisonType.Equals)
            {
                Comparisons.Add(node);
            }
        }
    }

    private sealed record TableReference(string? Alias, string Name, IReadOnlySet<string> Columns, TSqlFragment Node)
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
