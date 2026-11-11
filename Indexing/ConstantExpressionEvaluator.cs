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
            return CompareNumericValues(comparison.ComparisonType, firstNumber, secondNumber);
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

    private static bool TryNumericExpressionValue(ScalarExpression expression, out decimal value)
    {
        if (TryNumericLiteralValue(expression, out value))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryNumericExpressionValue(parenthesis.Expression, out value);
        }

        if (expression is UnaryExpression unary
            && TryNumericExpressionValue(unary.Expression, out var operand))
        {
            try
            {
                value = unary.UnaryExpressionType switch
                {
                    UnaryExpressionType.Positive => operand,
                    UnaryExpressionType.Negative => -operand,
                    _ => throw new InvalidOperationException(),
                };

                return true;
            }

            catch (OverflowException)
            {
                value = 0;

                return false;
            }

            catch (InvalidOperationException)
            {
                value = 0;

                return false;
            }
        }

        if (expression is not BinaryExpression binary
            || !TryNumericExpressionValue(binary.FirstExpression, out var first)
            || !TryNumericExpressionValue(binary.SecondExpression, out var second))
        {
            value = 0;

            return false;
        }

        try
        {
            value = binary.BinaryExpressionType switch
            {
                BinaryExpressionType.Add => first + second,
                BinaryExpressionType.Subtract => first - second,
                BinaryExpressionType.Multiply => first * second,
                BinaryExpressionType.Divide when second != 0 => first / second,
                BinaryExpressionType.Modulo when second != 0 => first % second,
                _ => throw new InvalidOperationException(),
            };

            return true;
        }

        catch (OverflowException)
        {
            value = 0;

            return false;
        }

        catch (DivideByZeroException)
        {
            value = 0;

            return false;
        }

        catch (InvalidOperationException)
        {
            value = 0;

            return false;
        }
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

}
