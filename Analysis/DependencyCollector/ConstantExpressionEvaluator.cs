using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Evaluates only SQL boolean expressions whose operands are literal constants.</summary>
internal static partial class ConstantExpressionEvaluator
{
    internal static bool? Evaluate(BooleanExpression expression)
    {
        if (TryEvaluateStructured(expression, out var structuredResult))
        {
            return structuredResult;
        }

        return EvaluateComparison(expression as BooleanComparisonExpression);
    }

    private static bool TryEvaluateStructured(BooleanExpression expression, out bool? result)
    {
        switch (expression)
        {
            case BooleanParenthesisExpression parenthesis:
                result = Evaluate(parenthesis.Expression);

                return true;

            case BooleanNotExpression not:
                var value = Evaluate(not.Expression);
                result = value is null ? null : !value.Value;

                return true;

            case BooleanBinaryExpression binary:
                result = EvaluateBinary(binary);

                return true;

            case BooleanIsNullExpression { Expression: NullLiteral } isNull:
                result = !isNull.IsNot;

                return true;

            case BooleanTernaryExpression ternary:
                return TryEvaluateBetween(ternary, out result);

            case InPredicate inPredicate:
                return TryEvaluateInPredicate(inPredicate, out result);
        }

        result = null;

        return false;
    }

    private static bool? EvaluateBinary(BooleanBinaryExpression expression)
    {
        var first = Evaluate(expression.FirstExpression);
        var second = Evaluate(expression.SecondExpression);

        return CombineBooleanValues(expression.BinaryExpressionType, first, second);
    }

    private static bool? CombineBooleanValues(
        BooleanBinaryExpressionType expressionType,
        bool? first,
        bool? second)
    {
        if (expressionType == BooleanBinaryExpressionType.And)
        {
            if (first == false || second == false)
            {
                return false;
            }

            return first == true && second == true ? true : null;
        }

        if (first == true || second == true)
        {
            return true;
        }

        return first == false && second == false ? false : null;
    }

    private static bool TryEvaluateBetween(BooleanTernaryExpression expression, out bool? result)
    {
        if (!TryNumericLiteralValue(expression.FirstExpression, out var value))
        {
            result = null;

            return false;
        }

        if (!TryNumericLiteralValue(expression.SecondExpression, out var lower))
        {
            result = null;

            return false;
        }

        if (!TryNumericLiteralValue(expression.ThirdExpression, out var upper))
        {
            result = null;

            return false;
        }

        var between = value >= lower && value <= upper;
        result = expression.TernaryExpressionType == BooleanTernaryExpressionType.Between ? between : !between;

        return true;
    }

    private static bool TryEvaluateInPredicate(InPredicate expression, out bool? result)
    {
        if (expression.Subquery is not null)
        {
            result = null;

            return false;
        }

        if (!TryNumericLiteralValue(expression.Expression, out var candidate))
        {
            result = null;

            return false;
        }

        if (expression.Values.Count == 0)
        {
            result = null;

            return false;
        }

        List<decimal> values = [];

        foreach (var item in expression.Values)
        {
            if (!TryNumericLiteralValue(item, out var itemValue))
            {
                result = null;

                return false;
            }

            values.Add(itemValue);
        }

        var contains = values.Contains(candidate);
        result = expression.NotDefined ? !contains : contains;

        return true;
    }

    private static bool? EvaluateComparison(BooleanComparisonExpression? comparison)
    {
        if (comparison is null)
        {
            return null;
        }

        if (TryNumericExpressionValue(comparison.FirstExpression, out var firstNumber)
            && TryNumericExpressionValue(comparison.SecondExpression, out var secondNumber))
        {
            return CompareNumericValues(
                comparison.ComparisonType,
                firstNumber.Value,
                secondNumber.Value);
        }

        if (TryTemporalLiteralValue(comparison.FirstExpression, out var firstTemporal)
            && TryTemporalLiteralValue(comparison.SecondExpression, out var secondTemporal))
        {
            return CompareTemporalValues(
                comparison.ComparisonType,
                firstTemporal,
                secondTemporal);
        }

        if (!TryLiteralValue(comparison.FirstExpression, out var firstValue))
        {
            return null;
        }

        if (!TryLiteralValue(comparison.SecondExpression, out var secondValue))
        {
            return null;
        }

        if (!string.Equals(firstValue, secondValue, StringComparison.Ordinal))
        {
            return null;
        }

        return comparison.ComparisonType switch
        {
            BooleanComparisonType.Equals => true,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => false,
            _ => null,
        };
    }

    private static bool? CompareNumericValues(
        BooleanComparisonType comparisonType,
        decimal first,
        decimal second)
    {
        return comparisonType switch
        {
            BooleanComparisonType.Equals => first == second,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => first != second,
            BooleanComparisonType.GreaterThan => first > second,
            BooleanComparisonType.LessThan => first < second,
            BooleanComparisonType.GreaterThanOrEqualTo => first >= second,
            BooleanComparisonType.LessThanOrEqualTo => first <= second,
            BooleanComparisonType.NotLessThan => first >= second,
            BooleanComparisonType.NotGreaterThan => first <= second,
            _ => null,
        };
    }

    internal static bool TryLiteralValue(ScalarExpression expression, out string value)
    {
        value = expression switch
        {
            NumericLiteral numeric => numeric.Value,
            IntegerLiteral integer => integer.Value,
            RealLiteral real => real.Value,
            MoneyLiteral money => money.Value,
            StringLiteral text => text.Value,
            _ => "",
        };

        return expression is NumericLiteral
            or IntegerLiteral
            or RealLiteral
            or MoneyLiteral
            or StringLiteral;
    }

}
