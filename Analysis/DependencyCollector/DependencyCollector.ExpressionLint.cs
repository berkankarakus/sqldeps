using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
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

    private static bool HasNullOperand(BooleanComparisonExpression node)
    {
        return node.FirstExpression is NullLiteral || node.SecondExpression is NullLiteral;
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
