using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

public sealed partial class DependencyCollector
{
    public override void Visit(QuerySpecification node)
    {
        var scope = DirectTableAliasFinder.FindScope(node.FromClause);
        var tables = scope.Tables;
        var aliases = scope.Aliases;
        ReportUnfilteredQuery(node, tables);

        var variableAliases = VariableAliases(node);
        var knownAliases = KnownAliases(aliases, variableAliases);

        ReportDuplicateAliases(aliases);
        ReportUngroupedSelectColumns(node);
        AddSelectColumnUsages(node, tables, knownAliases, variableAliases);
    }

    private static HashSet<string> VariableAliases(QuerySpecification query)
    {
        return VariableTableReferenceFinder.Find(query.FromClause)
            .Select(reference => reference.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> KnownAliases(
        IReadOnlyList<TableReferenceWithAlias> aliases,
        IReadOnlySet<string> variableAliases)
    {
        var knownAliases = aliases
            .Select(table => table.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        knownAliases.UnionWith(variableAliases);

        return knownAliases;
    }

    private void AddSelectColumnUsages(
        QuerySpecification query,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases)
    {
        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
        {
            AddSelectColumnUsage(query, element, tables, knownAliases, variableAliases);
        }
    }

    private void AddSelectColumnUsage(
        QuerySpecification query,
        SelectScalarExpression element,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases)
    {
        if (element.Expression is not ColumnReferenceExpression column)
        {
            return;
        }

        ValidateAliasReference(query, column, tables, knownAliases, variableAliases);

        if (!TryResolveColumnTable(column, tables, out var table))
        {
            return;
        }

        if (!TryGetColumnName(column, out var name))
        {
            return;
        }

        AddColumnUsage(table, name, "SELECT", column);
    }

    private void ReportUnfilteredQuery(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        if (HasQueryRestriction(query, tables))
        {
            return;
        }

        AddLint(
            "unfiltered-select",
            "info",
            query,
            "SELECT has no WHERE or TOP filter and may read many rows; verify that a broad result set is intentional."
        );
    }

    private static bool HasQueryRestriction(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        if (query.WhereClause is not null)
        {
            return true;
        }

        if (query.TopRowFilter is not null)
        {
            return true;
        }

        return tables.Count == 0;
    }

    private void ReportUngroupedSelectColumns(QuerySpecification query)
    {
        if (query.GroupByClause is null)
        {
            return;
        }

        var groupedColumns = query.GroupByClause.GroupingSpecifications
            .OfType<ExpressionGroupingSpecification>()
            .SelectMany(specification => UngroupedColumns(specification.Expression))
            .Select(ColumnReferenceText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupedColumnNameCounts = groupedColumns
            .GroupBy(ObjectNames.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var column in query.SelectElements
            .OfType<SelectScalarExpression>()
            .SelectMany(element => UngroupedColumns(element.Expression)))
        {
            var name = ColumnReferenceText(column);
            var columnName = ObjectNames.BaseName(name);
            if (IsGroupedColumn(column, name, columnName, groupedColumns, groupedColumnNameCounts))
            {
                continue;
            }

            AddLint("ungrouped-select-column", "error", column, $"Column '{name}' appears in SELECT but is not included in GROUP BY or an aggregate.");
        }
    }

    /// <summary>Returns columns that participate in an expression outside aggregate function arguments.</summary>
    private static IEnumerable<ColumnReferenceExpression> UngroupedColumns(ScalarExpression? expression)
    {
        if (expression is null)
        {
            return [];
        }

        ColumnReferenceVisitor visitor = new();
        expression.Accept(visitor);

        return visitor.Columns.Where(column => !visitor.IsInsideAggregate(column));
    }

    private static bool IsGroupedColumn(
        ColumnReferenceExpression column,
        string name,
        string columnName,
        IReadOnlySet<string> groupedColumns,
        IReadOnlyDictionary<string, int> groupedColumnNameCounts)
    {
        if (groupedColumns.Contains(name))
        {
            return true;
        }

        if (column.MultiPartIdentifier?.Identifiers.Count == 1
            && groupedColumnNameCounts.GetValueOrDefault(columnName) == 1)
        {
            return true;
        }

        return column.MultiPartIdentifier?.Identifiers.Count > 1
            && groupedColumns.Contains(columnName);
    }

    private sealed class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Columns { get; } = [];

        private readonly List<TSqlFragment> _aggregates = [];
        private readonly List<TSqlFragment> _subqueries = [];
        private readonly HashSet<ColumnReferenceExpression> _dateParts = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value == "*")
            {
                return;
            }

            Columns.Add(node);
        }

        public override void Visit(FunctionCall node)
        {
            var functionName = node.FunctionName?.Value;
            if (functionName is not null && AggregateFunctions.Contains(functionName))
            {
                _aggregates.Add(node);
            }

            if (TryGetDatePart(node, functionName, out var datePart))
            {
                _dateParts.Add(datePart);
            }

            base.Visit(node);
        }

        public override void Visit(ScalarSubquery node)
        {
            _subqueries.Add(node);
            base.Visit(node);
        }

        private static bool TryGetDatePart(
            FunctionCall node,
            string? functionName,
            out ColumnReferenceExpression datePart)
        {
            datePart = null!;

            if (functionName is null)
            {
                return false;
            }

            if (!DatePartFunctions.Contains(functionName))
            {
                return false;
            }

            if (node.Parameters.FirstOrDefault() is not ColumnReferenceExpression column)
            {
                return false;
            }

            datePart = column;

            return true;
        }

        public bool IsInsideAggregate(ColumnReferenceExpression column) => _aggregates.Any(aggregate =>
            column.StartOffset >= aggregate.StartOffset
            && column.StartOffset + column.FragmentLength <= aggregate.StartOffset + aggregate.FragmentLength)
            || _subqueries.Any(subquery =>
                column.StartOffset >= subquery.StartOffset
                && column.StartOffset + column.FragmentLength <= subquery.StartOffset + subquery.FragmentLength)
            || _dateParts.Contains(column);
    }

    private void ReportDuplicateAliases(IReadOnlyList<TableReferenceWithAlias> tables)
    {
        foreach (var duplicates in tables
            .Where(table => table.Alias is not null)
            .GroupBy(table => table.Alias!.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var duplicate in duplicates.Skip(1))
            {
                AddLint("duplicate-alias", "error", duplicate.Alias!, $"Duplicate table alias '{duplicates.Key}' in this query scope.");
            }
        }
    }

    private void ValidateAliasReference(
        QuerySpecification query,
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases
    )
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count < 2)
        {
            return;
        }

        var alias = identifiers[0].Value;
        if (variableAliases.Contains(alias))
        {
            return;
        }

        var aliasIsKnown = knownAliases.Contains(alias)
            || IsKnownAlias(tables, alias)
            || IsCorrelatedAlias(query, alias);
        if (!aliasIsKnown && TableUsingQualifier(tables, identifiers) is { } aliasedTable)
        {
            ReportTableNameAfterAlias(column, identifiers, aliasedTable);

            return;
        }

        if (identifiers.Count == 2)
        {
            ReportTwoPartAliasReference(column, tables, alias, aliasIsKnown);
            return;
        }

        if (aliasIsKnown)
        {
            ReportInvalidMultiPartAliasReference(column, tables, alias);
        }
    }

    private void ReportTwoPartAliasReference(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        bool qualifierIsAlias
    )
    {
        if (qualifierIsAlias || HasUnaliasedTable(tables, qualifier))
        {
            return;
        }

        ReportUnknownAlias(column, qualifier);
    }

    private void ReportTableNameAfterAlias(
        ColumnReferenceExpression column,
        IList<Identifier> identifiers,
        NamedTableReference aliasedTable
    )
    {
        var message = $"The multi-part identifier '{ReferenceText(column)}' could not be bound: "
            + $"table '{QualifierText(identifiers)}' is known as alias '{aliasedTable.Alias!.Value}' in this query.";
        AddLint("table-name-after-alias", "error", column, message);
    }

    private void ReportUnknownAlias(ColumnReferenceExpression column, string qualifier)
    {
        var message = $"The multi-part identifier '{ReferenceText(column)}' could not be bound: "
            + $"'{qualifier}' is not a known table alias in this query.";
        AddLint("unbound-identifier", "error", column, message);
    }

    private void ReportInvalidMultiPartAliasReference(ColumnReferenceExpression column, IReadOnlyList<NamedTableReference> tables, string alias)
    {
        var identifiers = column.MultiPartIdentifier!.Identifiers;
        var nextPartIsAlias = IsKnownAlias(tables, identifiers[1].Value);
        var message = InvalidMultiPartAliasMessage(column, identifiers, alias, nextPartIsAlias);
        AddLint("invalid-alias-reference", "error", column, message);
    }

    private static string InvalidMultiPartAliasMessage(
        ColumnReferenceExpression column,
        IList<Identifier> identifiers,
        string alias,
        bool nextPartIsAlias
    )
    {
        if (nextPartIsAlias)
        {
            return $"Invalid alias reference '{ReferenceText(column)}': '{alias}' is a table alias. A comma may be missing before '{identifiers[1].Value}.{identifiers[^1].Value}'.";
        }

        return $"Invalid alias reference '{ReferenceText(column)}': table alias '{alias}' can qualify only one column.";
    }

    private static bool IsKnownAlias(IEnumerable<NamedTableReference> tables, string alias)
    {
        return tables.Any(table => string.Equals(
            table.Alias?.Value,
            alias,
            StringComparison.OrdinalIgnoreCase
        ));
    }

    private bool IsCorrelatedAlias(QuerySpecification query, string alias)
    {
        if (!_correlatedAliases.TryGetValue(query, out var aliases))
        {
            return false;
        }

        return aliases.Contains(alias);
    }

    private static IReadOnlyDictionary<QuerySpecification, IReadOnlySet<string>> BuildCorrelatedAliases(TSqlStatement statement)
    {
        var queries = QuerySpecificationFinder.Find(statement);
        var aliasesByQuery = queries.ToDictionary(
            query => query,
            query => DirectTableAliasFinder.FindScope(query.FromClause).Aliases
                .Select(reference => reference.Alias?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        var correlatedAliases = new Dictionary<QuerySpecification, IReadOnlySet<string>>();

        foreach (var query in queries)
        {
            correlatedAliases[query] = queries
                .Where(parent => parent != query && ContainsQuery(parent, query))
                .SelectMany(parent => aliasesByQuery[parent])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return correlatedAliases;
    }

    private static bool ContainsQuery(TSqlFragment outer, TSqlFragment inner)
    {
        int outerEnd = outer.StartOffset + outer.FragmentLength;
        int innerEnd = inner.StartOffset + inner.FragmentLength;

        if (inner.StartOffset < outer.StartOffset)
        {
            return false;
        }

        return innerEnd <= outerEnd;
    }

    private NamedTableReference? TableUsingQualifier(IEnumerable<NamedTableReference> tables, IList<Identifier> identifiers)
    {
        string qualifier = QualifierText(identifiers);

        return tables.FirstOrDefault(table => MatchesTableNameAfterAlias(table, qualifier));
    }

    private bool MatchesTableNameAfterAlias(NamedTableReference table, string qualifier)
    {
        if (table.Alias is null)
        {
            return false;
        }

        string fullName = ObjectNames.Format(table.SchemaObject, defaultSchema);

        if (string.Equals(fullName, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(table.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnaliasedTable(IEnumerable<NamedTableReference> tables, string name)
    {
        return tables.Any(table => IsUnaliasedTable(table, name));
    }

    private static bool IsUnaliasedTable(NamedTableReference table, string name)
    {
        if (table.Alias is not null)
        {
            return false;
        }

        return string.Equals(
            table.SchemaObject.BaseIdentifier.Value,
            name,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReferenceText(ColumnReferenceExpression column)
    {
        return string.Join(
            '.',
            column.MultiPartIdentifier!.Identifiers.Select(identifier => identifier.Value)
        );
    }

    private static string QualifierText(IList<Identifier> identifiers)
    {
        return string.Join(
            '.',
            identifiers.Take(identifiers.Count - 1).Select(identifier => identifier.Value)
        );
    }

    private static bool TryResolveColumnTable(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        out NamedTableReference table
    )
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;

        if (identifiers is null || identifiers.Count == 0)
        {
            table = null!;
            return false;
        }

        if (identifiers.Count == 1 && tables.Count == 1)
        {
            table = tables[0];
            return true;
        }

        if (!TryGetQualifier(identifiers, out var qualifier))
        {
            table = null!;
            return false;
        }

        return TryFindUniqueTable(tables, qualifier, out table);
    }

    private static bool TryGetQualifier(IList<Identifier> identifiers, out string qualifier)
    {
        if (identifiers.Count < 2)
        {
            qualifier = "";
            return false;
        }

        qualifier = identifiers[identifiers.Count - 2].Value;
        return true;
    }

    private static bool TryFindUniqueTable(
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        out NamedTableReference table
    )
    {
        table = null!;

        foreach (var reference in tables.Where(reference => MatchesQualifier(reference, qualifier)))
        {
            if (table is not null)
            {
                table = null!;
                return false;
            }

            table = reference;
        }

        return table is not null;
    }

    private static bool MatchesQualifier(NamedTableReference reference, string qualifier)
    {
        if (string.Equals(reference.Alias?.Value, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(reference.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetColumnName(ColumnReferenceExpression? column, out string name)
    {
        if (column is null)
        {
            name = "";
            return false;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count == 0)
        {
            name = "";
            return false;
        }

        name = identifiers[identifiers.Count - 1].Value;
        return true;
    }

    private static string ColumnReferenceText(ColumnReferenceExpression column) => column.MultiPartIdentifier is null
        ? ""
        : string.Join('.', column.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value));
}
