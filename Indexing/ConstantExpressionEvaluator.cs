using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>Evaluates only SQL boolean expressions whose operands are literal constants.</summary>
internal static class ConstantExpressionEvaluator
{
    public static bool? Evaluate(BooleanExpression expression)
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
        if (first is null || second is null)
        {
            return null;
        }

        return expression.BinaryExpressionType == BooleanBinaryExpressionType.And
            ? first.Value && second.Value
            : first.Value || second.Value;
    }

    private static bool TryEvaluateBetween(BooleanTernaryExpression expression, out bool? result)
    {
        if (!TryNumericLiteralValue(expression.FirstExpression, out var value)
            || !TryNumericLiteralValue(expression.SecondExpression, out var lower)
            || !TryNumericLiteralValue(expression.ThirdExpression, out var upper))
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
        if (expression.Subquery is not null
            || !TryNumericLiteralValue(expression.Expression, out var candidate)
            || expression.Values.Count == 0
            || expression.Values.Any(item => !TryNumericLiteralValue(item, out _)))
        {
            result = null;

            return false;
        }

        var contains = expression.Values.Any(item => TryNumericLiteralValue(item, out var itemValue) && itemValue == candidate);
        result = expression.NotDefined ? !contains : contains;

        return true;
    }

    private static bool? EvaluateComparison(BooleanComparisonExpression? comparison)
    {
        if (comparison is null
            || !TryLiteralValue(comparison.FirstExpression, out var firstValue)
            || !TryLiteralValue(comparison.SecondExpression, out var secondValue))
        {
            return null;
        }

        var comparisonResult = CompareLiteralValues(comparison, firstValue, secondValue);
        if (comparisonResult is null)
        {
            // String equality depends on the database collation. Only identical literal text is deterministic without collation metadata.
            return null;
        }

        return comparison.ComparisonType switch
        {
            BooleanComparisonType.Equals => comparisonResult.Value,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => !comparisonResult.Value,
            BooleanComparisonType.GreaterThan => CompareNumericLiterals(comparison, (left, right) => left > right),
            BooleanComparisonType.LessThan => CompareNumericLiterals(comparison, (left, right) => left < right),
            BooleanComparisonType.GreaterThanOrEqualTo => CompareNumericLiterals(comparison, (left, right) => left >= right),
            BooleanComparisonType.LessThanOrEqualTo => CompareNumericLiterals(comparison, (left, right) => left <= right),
            BooleanComparisonType.NotLessThan => CompareNumericLiterals(comparison, (left, right) => left >= right),
            BooleanComparisonType.NotGreaterThan => CompareNumericLiterals(comparison, (left, right) => left <= right),
            _ => null,
        };
    }

    private static bool? CompareLiteralValues(
        BooleanComparisonExpression comparison,
        string firstValue,
        string secondValue)
    {
        if (TryNumericLiteralValue(comparison.FirstExpression, out var firstNumber)
            && TryNumericLiteralValue(comparison.SecondExpression, out var secondNumber))
        {
            return firstNumber == secondNumber;
        }

        return string.Equals(firstValue, secondValue, StringComparison.Ordinal) ? true : null;
    }

    public static bool TryNumericLiteralValue(ScalarExpression expression, out decimal value)
    {
        var text = expression switch
        {
            NumericLiteral numeric => numeric.Value,
            IntegerLiteral integer => integer.Value,
            MoneyLiteral money => money.Value,
            _ => "",
        };

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryLiteralValue(ScalarExpression expression, out string value)
    {
        value = expression switch
        {
            NumericLiteral numeric => numeric.Value,
            IntegerLiteral integer => integer.Value,
            MoneyLiteral money => money.Value,
            StringLiteral text => text.Value,
            _ => "",
        };

        return expression is NumericLiteral or IntegerLiteral or MoneyLiteral or StringLiteral;
    }

    private static bool? CompareNumericLiterals(BooleanComparisonExpression comparison, Func<decimal, decimal, bool> comparer)
    {
        return TryNumericLiteralValue(comparison.FirstExpression, out var first)
            && TryNumericLiteralValue(comparison.SecondExpression, out var second)
                ? comparer(first, second)
                : null;
    }
}
