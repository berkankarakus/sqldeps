using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
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

    private static string ColumnReferenceText(ColumnReferenceExpression column) => column.MultiPartIdentifier is null
        ? ""
        : string.Join('.', column.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value));
}
