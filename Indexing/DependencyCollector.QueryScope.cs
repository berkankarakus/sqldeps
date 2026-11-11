using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

internal sealed partial class DependencyCollector
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
        var alwaysFalseFilter = AlwaysFalseFilter(query);
        if (alwaysFalseFilter is not null)
        {
            AddLint(
                "select-always-false-filter",
                "info",
                query,
                $"SELECT {alwaysFalseFilter} condition is always false and will return no rows."
            );

            return;
        }

        var alwaysTrueFilter = AlwaysTrueFilter(query);
        if (alwaysTrueFilter is not null)
        {
            AddLint(
                "select-ineffective-filter",
                "info",
                query,
                $"SELECT {alwaysTrueFilter} condition is always true and does not restrict the result set."
            );

            return;
        }

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

    private static string? AlwaysFalseFilter(QuerySpecification query)
    {
        if (query.WhereClause?.SearchCondition is { } whereCondition
            && ConstantExpressionEvaluator.Evaluate(whereCondition) == false)
        {
            return "WHERE";
        }

        if (query.HavingClause?.SearchCondition is { } havingCondition
            && ConstantExpressionEvaluator.Evaluate(havingCondition) == false)
        {
            return "HAVING";
        }

        return null;
    }

    private static string? AlwaysTrueFilter(QuerySpecification query)
    {
        if (query.WhereClause?.SearchCondition is { } whereCondition
            && ConstantExpressionEvaluator.Evaluate(whereCondition) == true)
        {
            return "WHERE";
        }

        if (query.HavingClause?.SearchCondition is { } havingCondition
            && ConstantExpressionEvaluator.Evaluate(havingCondition) == true)
        {
            return "HAVING";
        }

        return null;
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

        if (IsUniqueUnqualifiedGroup(column, columnName, groupedColumnNameCounts))
        {
            return true;
        }

        return IsQualifiedGroup(column, columnName, groupedColumns);
    }

    private static bool IsUniqueUnqualifiedGroup(
        ColumnReferenceExpression column,
        string columnName,
        IReadOnlyDictionary<string, int> groupedColumnNameCounts)
    {
        return column.MultiPartIdentifier?.Identifiers.Count == 1
            && groupedColumnNameCounts.GetValueOrDefault(columnName) == 1;
    }

    private static bool IsQualifiedGroup(
        ColumnReferenceExpression column,
        string columnName,
        IReadOnlySet<string> groupedColumns)
    {
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

        public bool IsInsideAggregate(ColumnReferenceExpression column)
        {
            if (IsInsideAny(column, _aggregates))
            {
                return true;
            }

            if (IsInsideAny(column, _subqueries))
            {
                return true;
            }

            return _dateParts.Contains(column);
        }

        private static bool IsInsideAny(
            ColumnReferenceExpression column,
            IEnumerable<TSqlFragment> fragments)
        {
            return fragments.Any(fragment =>
                column.StartOffset >= fragment.StartOffset
                && column.StartOffset + column.FragmentLength <= fragment.StartOffset + fragment.FragmentLength);
        }
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

    private static bool TryGetColumnName(ColumnReferenceExpression? column, out string name)
    {
        if (column is null)
        {
            name = "";
            return false;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null)
        {
            name = "";
            return false;
        }

        if (identifiers.Count == 0)
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
