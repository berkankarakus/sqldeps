using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class ConstantExpressionEvaluator
{
    public static bool TryNumericLiteralValue(ScalarExpression expression, out decimal value)
    {
        var text = expression switch
        {
            NumericLiteral numeric => numeric.Value,
            IntegerLiteral integer => integer.Value,
            RealLiteral real => real.Value,
            MoneyLiteral money => money.Value,
            _ => "",
        };

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

    private readonly record struct NumericValue(decimal Value, bool IsInteger);
}
