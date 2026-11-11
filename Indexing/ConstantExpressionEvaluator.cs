using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>Evaluates only SQL boolean expressions whose operands are literal constants.</summary>
internal static class ConstantExpressionEvaluator
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF",
        "yyyy'-'MM'-'dd HH':'mm':'ss",
        "yyyy'-'MM'-'dd HH':'mm':'ss.FFFFFFF",
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy'-'MM'-'dd",
        "yyyyMMdd",
    ];

    private static readonly string[] TimeFormats =
    [
        "hh':'mm",
        "hh':'mm':'ss",
        "hh':'mm':'ss.FFFFFFF",
    ];

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

    private static bool? CompareTemporalValues(
        BooleanComparisonType comparisonType,
        TemporalValue first,
        TemporalValue second)
    {
        if (first.Kind != second.Kind
            && !(first.Kind is TemporalKind.Date or TemporalKind.DateTime
                && second.Kind is TemporalKind.Date or TemporalKind.DateTime))
        {
            return null;
        }

        int comparison = first.Kind == TemporalKind.Time || second.Kind == TemporalKind.Time
            ? first.Time.CompareTo(second.Time)
            : first.DateTime.CompareTo(second.DateTime);

        return comparisonType switch
        {
            BooleanComparisonType.Equals => comparison == 0,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => comparison != 0,
            BooleanComparisonType.GreaterThan => comparison > 0,
            BooleanComparisonType.LessThan => comparison < 0,
            BooleanComparisonType.GreaterThanOrEqualTo => comparison >= 0,
            BooleanComparisonType.LessThanOrEqualTo => comparison <= 0,
            BooleanComparisonType.NotLessThan => comparison >= 0,
            BooleanComparisonType.NotGreaterThan => comparison <= 0,
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

    private static bool TryTemporalLiteralValue(ScalarExpression expression, out TemporalValue value)
    {
        value = default;

        if (expression is not StringLiteral literal)
        {
            return false;
        }

        if (DateTime.TryParseExact(
                literal.Value,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            value = new TemporalValue(dateTime, default, TemporalKind.DateTime);

            return true;
        }

        if (DateTime.TryParseExact(
                literal.Value,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            value = new TemporalValue(date, default, TemporalKind.Date);

            return true;
        }

        if (TimeSpan.TryParseExact(
                literal.Value,
                TimeFormats,
                CultureInfo.InvariantCulture,
                out var time))
        {
            value = new TemporalValue(default, time, TemporalKind.Time);

            return true;
        }

        return false;
    }

    private static bool TryNumericExpressionValue(ScalarExpression expression, out NumericValue value)
    {
        if (TryNumericLiteralValue(expression, out var literalValue))
        {
            value = new NumericValue(literalValue, IsIntegerLiteral(expression));

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
                var numericValue = unary.UnaryExpressionType switch
                {
                    UnaryExpressionType.Positive => operand,
                    UnaryExpressionType.Negative => new NumericValue(-operand.Value, operand.IsInteger),
                    _ => throw new InvalidOperationException(),
                };
                value = numericValue;

                return true;
            }

            catch (OverflowException)
            {
                value = default;

                return false;
            }

            catch (InvalidOperationException)
            {
                value = default;

                return false;
            }
        }

        if (expression is not BinaryExpression binary
            || !TryNumericExpressionValue(binary.FirstExpression, out var first)
            || !TryNumericExpressionValue(binary.SecondExpression, out var second))
        {
            value = default;

            return false;
        }

        try
        {
            value = binary.BinaryExpressionType switch
            {
                BinaryExpressionType.Add => new NumericValue(first.Value + second.Value, first.IsInteger && second.IsInteger),
                BinaryExpressionType.Subtract => new NumericValue(first.Value - second.Value, first.IsInteger && second.IsInteger),
                BinaryExpressionType.Multiply => new NumericValue(first.Value * second.Value, first.IsInteger && second.IsInteger),
                BinaryExpressionType.Divide when second.Value != 0 => Divide(first, second),
                BinaryExpressionType.Modulo when second.Value != 0 => new NumericValue(first.Value % second.Value, first.IsInteger && second.IsInteger),
                _ => throw new InvalidOperationException(),
            };

            return true;
        }

        catch (OverflowException)
        {
            value = default;

            return false;
        }

        catch (DivideByZeroException)
        {
            value = default;

            return false;
        }

        catch (InvalidOperationException)
        {
            value = default;

            return false;
        }
    }

    private static NumericValue Divide(NumericValue first, NumericValue second)
    {
        var result = first.Value / second.Value;

        if (first.IsInteger && second.IsInteger)
        {
            result = decimal.Truncate(result);
        }

        return new NumericValue(result, first.IsInteger && second.IsInteger);
    }

    private static bool IsIntegerLiteral(ScalarExpression expression)
    {
        if (expression is IntegerLiteral)
        {
            return true;
        }

        if (expression is not NumericLiteral numeric)
        {
            return false;
        }

        return !numeric.Value.Contains('.', StringComparison.Ordinal)
            && !numeric.Value.Contains('e', StringComparison.OrdinalIgnoreCase);
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

    private readonly record struct NumericValue(decimal Value, bool IsInteger);

    private readonly record struct TemporalValue(DateTime DateTime, TimeSpan Time, TemporalKind Kind);

    private enum TemporalKind
    {
        Date,
        DateTime,
        Time,
    }

}
