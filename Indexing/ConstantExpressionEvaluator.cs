using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>Evaluates only SQL boolean expressions whose operands are literal constants.</summary>
internal static class ConstantExpressionEvaluator
{
    public static bool? Evaluate(BooleanExpression expression)
    {
        if (expression is BooleanParenthesisExpression parenthesis)
        {
            return Evaluate(parenthesis.Expression);
        }

        if (expression is BooleanNotExpression not)
        {
            var notValue = Evaluate(not.Expression);
            return notValue is null ? null : !notValue.Value;
        }

        if (expression is BooleanBinaryExpression binary)
        {
            var first = Evaluate(binary.FirstExpression);
            var second = Evaluate(binary.SecondExpression);
            if (first is null || second is null)
            {
                return null;
            }

            return binary.BinaryExpressionType == BooleanBinaryExpressionType.And
                ? first.Value && second.Value
                : first.Value || second.Value;
        }

        if (expression is BooleanIsNullExpression isNull && isNull.Expression is NullLiteral)
        {
            return !isNull.IsNot;
        }

        if (expression is BooleanTernaryExpression ternary
            && TryNumericLiteralValue(ternary.FirstExpression, out var value)
            && TryNumericLiteralValue(ternary.SecondExpression, out var lower)
            && TryNumericLiteralValue(ternary.ThirdExpression, out var upper))
        {
            var between = value >= lower && value <= upper;
            return ternary.TernaryExpressionType == BooleanTernaryExpressionType.Between ? between : !between;
        }

        if (expression is InPredicate inPredicate
            && inPredicate.Subquery is null
            && TryNumericLiteralValue(inPredicate.Expression, out var candidate)
            && inPredicate.Values.Count > 0
            && inPredicate.Values.All(item => TryNumericLiteralValue(item, out _)))
        {
            var contains = inPredicate.Values.Any(item => TryNumericLiteralValue(item, out var itemValue) && itemValue == candidate);
            return inPredicate.NotDefined ? !contains : contains;
        }

        if (expression is not BooleanComparisonExpression comparison
            || !TryLiteralValue(comparison.FirstExpression, out var firstValue)
            || !TryLiteralValue(comparison.SecondExpression, out var secondValue))
        {
            return null;
        }

        bool? comparisonResult = TryNumericLiteralValue(comparison.FirstExpression, out var firstNumber)
            && TryNumericLiteralValue(comparison.SecondExpression, out var secondNumber)
                ? firstNumber == secondNumber
                : string.Equals(firstValue, secondValue, StringComparison.Ordinal)
                    ? true
                    : (bool?)null;
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
