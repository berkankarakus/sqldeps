using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

public sealed partial class DependencyCollector
{
    public override void Visit(QuerySpecification node)
    {
        var tables = DirectNamedTableReferenceFinder.Find(node.FromClause);
        var aliases = DirectTableAliasFinder.Find(node.FromClause);
        ReportUnfilteredQuery(node, tables);

        foreach (var alias in tables.Select(table => table.Alias?.Value).OfType<string>())
        {
            _batchAliases.Add(alias);
        }

        foreach (var alias in aliases.Select(table => table.Alias?.Value).OfType<string>())
        {
            _batchAliases.Add(alias);
        }

        var variableAliases = VariableTableReferenceFinder.Find(node.FromClause)
            .Select(reference => reference.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownAliases = new HashSet<string>(_batchAliases, StringComparer.OrdinalIgnoreCase);
        knownAliases.UnionWith(variableAliases);
        foreach (var alias in aliases.Select(table => table.Alias?.Value).OfType<string>())
        {
            knownAliases.Add(alias);
        }

        ReportDuplicateAliases(aliases);
        ReportUngroupedSelectColumns(node);

        foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
        {
            if (element.Expression is not ColumnReferenceExpression column)
            {
                continue;
            }

            ValidateAliasReference(column, tables, knownAliases, variableAliases);

            if (!TryResolveColumnTable(column, tables, out var table))
            {
                continue;
            }

            if (!TryGetColumnName(column, out var name))
            {
                continue;
            }

            AddColumnUsage(table, name, "SELECT", column);
        }
    }

    private void ReportUnfilteredQuery(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        if (query.WhereClause is not null || query.TopRowFilter is not null || tables.Count == 0)
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
            .GroupBy(column => column.Split('.').Last(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var column in query.SelectElements
            .OfType<SelectScalarExpression>()
            .SelectMany(element => UngroupedColumns(element.Expression)))
        {
            var name = ColumnReferenceText(column);
            var columnName = name.Split('.').Last();
            if (groupedColumns.Contains(name)
                || column.MultiPartIdentifier?.Identifiers.Count == 1
                    && groupedColumnNameCounts.GetValueOrDefault(columnName) == 1
                || column.MultiPartIdentifier?.Identifiers.Count > 1
                    && groupedColumns.Contains(columnName))
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
            if (functionName is not null
                && AggregateFunctions.Contains(functionName))
            {
                _aggregates.Add(node);
            }

            if (functionName is not null
                && DatePartFunctions.Contains(functionName)
                && node.Parameters.FirstOrDefault() is ColumnReferenceExpression datePart)
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

        var aliasIsKnown = knownAliases.Contains(alias) || IsKnownAlias(tables, alias);
        if (!aliasIsKnown && TableUsingQualifier(tables, identifiers) is { } aliasedTable)
        {
            AddLint("table-name-after-alias", "error", column, $"The multi-part identifier '{ReferenceText(column)}' could not be bound: table '{QualifierText(identifiers)}' is known as alias '{aliasedTable.Alias!.Value}' in this query.");
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

        AddLint("unbound-identifier", "error", column, $"The multi-part identifier '{ReferenceText(column)}' could not be bound: '{qualifier}' is not a known table alias in this query.");
    }

    private void ReportInvalidMultiPartAliasReference(ColumnReferenceExpression column, IReadOnlyList<NamedTableReference> tables, string alias)
    {
        var identifiers = column.MultiPartIdentifier!.Identifiers;
        var nextPartIsAlias = tables.Any(table => string.Equals(table.Alias?.Value, identifiers[1].Value, StringComparison.OrdinalIgnoreCase));
        var message = nextPartIsAlias
            ? $"Invalid alias reference '{ReferenceText(column)}': '{alias}' is a table alias. A comma may be missing before '{identifiers[1].Value}.{identifiers[^1].Value}'."
            : $"Invalid alias reference '{ReferenceText(column)}': table alias '{alias}' can qualify only one column.";
        AddLint("invalid-alias-reference", "error", column, message);
    }

    private static bool IsKnownAlias(IEnumerable<NamedTableReference> tables, string alias) => tables.Any(table => string.Equals(table.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase));

    private NamedTableReference? TableUsingQualifier(IEnumerable<NamedTableReference> tables, IList<Identifier> identifiers)
    {
        var qualifier = QualifierText(identifiers);
        return tables.FirstOrDefault(table => table.Alias is not null
            && (string.Equals(ObjectNames.Format(table.SchemaObject, defaultSchema), qualifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasUnaliasedTable(IEnumerable<NamedTableReference> tables, string name) => tables.Any(table => table.Alias is null && string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase));

    private static string ReferenceText(ColumnReferenceExpression column) => string.Join('.', column.MultiPartIdentifier!.Identifiers.Select(identifier => identifier.Value));

    private static string QualifierText(IList<Identifier> identifiers) => string.Join('.', identifiers.Take(identifiers.Count - 1).Select(identifier => identifier.Value));

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

    private static bool MatchesQualifier(NamedTableReference reference, string qualifier) =>
        string.Equals(reference.Alias?.Value, qualifier, StringComparison.OrdinalIgnoreCase)
        || string.Equals(reference.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);

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
