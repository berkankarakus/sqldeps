using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    public override void Visit(BinaryQueryExpression node)
    {
        var firstCount = SelectElementCount(node.FirstQueryExpression);
        var secondCount = SelectElementCount(node.SecondQueryExpression);

        if (firstCount is null || secondCount is null)
        {
            return;
        }

        if (firstCount == secondCount)
        {
            return;
        }

        AddLint("set-operation-column-count", "error", node, $"{node.BinaryQueryExpressionType} requires both queries to return the same number of columns ({firstCount} and {secondCount}).");
    }

    public override void Visit(SelectStarExpression node)
    {
        AddLint("select-star", "info", node, "SELECT * makes result shape and schema changes harder to reason about.");
    }

    public override void Visit(BooleanComparisonExpression node)
    {
        ReportNullComparison(node);

        ReportNonSargableComparison(node);
    }

    private void ReportNonSargableComparison(BooleanComparisonExpression node)
    {
        if (!TryGetComparisonColumn(node, out var column))
        {
            return;
        }

        AddLint("non-sargable-function", "info", column, "A function is applied to a column in a comparison; this can prevent an index seek. Compare the raw column when possible.");
    }

    private static bool TryGetComparisonColumn(
        BooleanComparisonExpression comparison,
        out ColumnReferenceExpression column)
    {
        if (TryGetWrappedColumn(comparison.FirstExpression, out column))
        {
            return true;
        }

        return TryGetWrappedColumn(comparison.SecondExpression, out column);
    }

    private void ReportNullComparison(BooleanComparisonExpression node)
    {
        if (!HasNullOperand(node))
        {
            return;
        }

        var replacement = node.ComparisonType == BooleanComparisonType.Equals ? "IS NULL" : "IS NOT NULL";

        if (node.ComparisonType is BooleanComparisonType.Equals
            or BooleanComparisonType.NotEqualToBrackets
            or BooleanComparisonType.NotEqualToExclamation)
        {
            AddLint("null-comparison", "warning", node, $"Comparison with NULL never evaluates as expected; use {replacement} instead.");
        }
    }

    private static bool TryGetWrappedColumn(ScalarExpression expression, out ColumnReferenceExpression column)
    {
        if (expression is not FunctionCall function)
        {
            column = null!;

            return false;
        }

        if (function.Parameters.OfType<ColumnReferenceExpression>().FirstOrDefault() is { } reference)
        {
            column = reference;

            return true;
        }

        column = null!;

        return false;
    }

    public override void Visit(LikePredicate node)
    {
        if (node.SecondExpression is not StringLiteral literal)
        {
            return;
        }

        if (literal.Value.Length == 0)
        {
            return;
        }

        if (literal.Value[0] != '%')
        {
            return;
        }

        AddLint("leading-wildcard-like", "info", node, "LIKE pattern starts with '%', which usually prevents an index seek.");
    }

    public override void Visit(InPredicate node)
    {
        if (!node.NotDefined)
        {
            return;
        }

        if (!node.Values.Any(value => value is NullLiteral))
        {
            return;
        }

        AddLint("not-in-null", "warning", node, "NOT IN with NULL never matches; remove NULL or use NOT EXISTS.");
    }

    public override void Visit(SearchedCaseExpression node)
    {
        ReportMixedCaseConstants(node, node.WhenClauses);
    }

    public override void Visit(SimpleCaseExpression node)
    {
        ReportMixedCaseConstants(node, node.WhenClauses);
    }

    public override void Visit(TopRowFilter node)
    {
        if (TryIntegerLiteral(node.Expression, out var count) && count == 0)
        {
            AddLint("top-zero", "info", node, "TOP (0) always returns no rows; use it only when an empty result is intentional.");
        }
    }

    public override void Visit(OffsetClause node)
    {
        if (TryIntegerLiteral(node.OffsetExpression, out var offset) && offset < 0)
        {
            AddLint("negative-offset", "error", node.OffsetExpression, "OFFSET cannot be negative.");
        }

        if (node.FetchExpression is null)
        {
            return;
        }

        if (!TryIntegerLiteral(node.FetchExpression, out var fetch))
        {
            return;
        }

        if (fetch <= 0)
        {
            AddLint("invalid-fetch", "error", node.FetchExpression, "FETCH row count must be greater than zero.");
        }
    }

    public override void Visit(CommonTableExpression node)
    {
        var selectCount = SelectElementCount(node.QueryExpression);

        if (node.Columns.Count == 0 || selectCount is null)
        {
            return;
        }

        if (node.Columns.Count == selectCount)
        {
            return;
        }

        AddLint(
            "cte-column-count",
            "error",
            node,
            $"CTE '{node.ExpressionName.Value}' declares {node.Columns.Count} column(s) but its query returns {selectCount}."
        );
    }

    private static bool HasNullOperand(BooleanComparisonExpression node)
    {
        return node.FirstExpression is NullLiteral || node.SecondExpression is NullLiteral;
    }

    private void ReportMixedCaseConstants(CaseExpression node, IEnumerable<WhenClause> clauses)
    {
        var types = clauses
            .Select(clause => ConstantType(clause.ThenExpression))
            .Append(ConstantType(node.ElseExpression))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (types.Length > 1)
        {
            AddLint("case-mixed-constants", "info", node, $"CASE returns mixed constant types ({string.Join(" and ", types)}); SQL Server will apply an implicit conversion.");
        }
    }

    private static string? ConstantType(ScalarExpression? expression) => expression switch
    {
        StringLiteral => "string",
        IntegerLiteral or NumericLiteral or MoneyLiteral => "number",
        NullLiteral => null,
        _ => null,
    };
}
