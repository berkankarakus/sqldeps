using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class ConstantExpressionEvaluator
{
    public static bool? Evaluate(
        BooleanExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        var result = Evaluate(expression);
        if (result is not null)
        {
            return result;
        }

        return EvaluateKnownVariables(expression, variables);
    }

    private static bool? EvaluateKnownVariables(
        BooleanExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        return expression switch
        {
            BooleanParenthesisExpression parenthesis => Evaluate(parenthesis.Expression, variables),
            BooleanNotExpression not => Negate(Evaluate(not.Expression, variables)),
            BooleanBinaryExpression binary => EvaluateBinary(binary, variables),
            BooleanIsNullExpression isNull => EvaluateIsNull(isNull, variables),
            BooleanComparisonExpression comparison => EvaluateComparisonWithVariables(comparison, variables),
            BooleanTernaryExpression ternary => EvaluateBetweenWithVariables(ternary, variables),
            InPredicate inPredicate => EvaluateInWithVariables(inPredicate, variables),
            _ => null,
        };
    }

    private static bool? EvaluateBinary(
        BooleanBinaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        var first = Evaluate(expression.FirstExpression, variables);
        var second = Evaluate(expression.SecondExpression, variables);

        return CombineBooleanValues(expression.BinaryExpressionType, first, second);
    }

    private static bool? EvaluateIsNull(
        BooleanIsNullExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (!TryResolveLiteral(expression.Expression, variables, out var value))
        {
            return null;
        }

        return value is NullLiteral ? !expression.IsNot : expression.IsNot;
    }

    private static bool? EvaluateComparisonWithVariables(
        BooleanComparisonExpression comparison,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (TryNumericExpressionValue(comparison.FirstExpression, variables, out var firstNumber)
            && TryNumericExpressionValue(comparison.SecondExpression, variables, out var secondNumber))
        {
            return CompareNumericValues(comparison.ComparisonType, firstNumber, secondNumber);
        }

        if (TryStringExpressionValue(comparison.FirstExpression, variables, out var firstText)
            && TryStringExpressionValue(comparison.SecondExpression, variables, out var secondText))
        {
            var stringResult = CompareStringValues(comparison.ComparisonType, firstText, secondText);
            if (stringResult is not null)
            {
                return stringResult;
            }
        }

        if (!TryTemporalExpressionValue(comparison.FirstExpression, variables, out var firstTemporal)
            || !TryTemporalExpressionValue(comparison.SecondExpression, variables, out var secondTemporal))
        {
            return null;
        }

        return CompareTemporalValues(comparison.ComparisonType, firstTemporal, secondTemporal);
    }

    private static bool? CompareStringValues(
        BooleanComparisonType comparisonType,
        string first,
        string second)
    {
        return comparisonType switch
        {
            BooleanComparisonType.Equals => string.Equals(first, second, StringComparison.Ordinal),
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation
                => !string.Equals(first, second, StringComparison.Ordinal),
            _ => null,
        };
    }

    private static bool? EvaluateBetweenWithVariables(
        BooleanTernaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (!TryNumericExpressionValue(expression.FirstExpression, variables, out var value)
            || !TryNumericExpressionValue(expression.SecondExpression, variables, out var lower)
            || !TryNumericExpressionValue(expression.ThirdExpression, variables, out var upper))
        {
            return null;
        }

        var between = value >= lower && value <= upper;

        return expression.TernaryExpressionType == BooleanTernaryExpressionType.Between
            ? between
            : !between;
    }

    private static bool? EvaluateInWithVariables(
        InPredicate expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (expression.Subquery is not null
            || !TryNumericExpressionValue(expression.Expression, variables, out var candidate)
            || expression.Values.Count == 0)
        {
            return null;
        }

        List<decimal> values = [];
        foreach (var item in expression.Values)
        {
            if (!TryNumericExpressionValue(item, variables, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        var contains = values.Contains(candidate);

        return expression.NotDefined ? !contains : contains;
    }

    private static bool TryTemporalExpressionValue(
        ScalarExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        out TemporalValue value)
    {
        if (expression is VariableReference variable
            && variables.TryGetValue(variable.Name, out var variableValue))
        {
            return TryTemporalLiteralValue(variableValue, out value);
        }

        if (TryTemporalLiteralValue(expression, out value))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryTemporalExpressionValue(parenthesis.Expression, variables, out value);
        }

        value = default;

        return false;
    }

    private static bool TryNumericExpressionValue(
        ScalarExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        out decimal value)
    {
        if (expression is VariableReference variable
            && variables.TryGetValue(variable.Name, out var variableValue))
        {
            return TryNumericLiteralValue(variableValue, out value);
        }

        if (TryNumericLiteralValue(expression, out value))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryNumericExpressionValue(parenthesis.Expression, variables, out value);
        }

        if (expression is UnaryExpression unary
            && TryNumericExpressionValue(unary.Expression, variables, out var operand))
        {
            if (unary.UnaryExpressionType == UnaryExpressionType.Positive)
            {
                value = operand;

                return true;
            }

            if (unary.UnaryExpressionType == UnaryExpressionType.Negative)
            {
                try
                {
                    value = -operand;
                }
                catch (OverflowException)
                {
                    value = 0;

                    return false;
                }

                return true;
            }
        }

        if (expression is BinaryExpression binary
            && TryNumericExpressionValue(binary.FirstExpression, variables, out var first)
            && TryNumericExpressionValue(binary.SecondExpression, variables, out var second))
        {
            try
            {
                switch (binary.BinaryExpressionType)
                {
                    case BinaryExpressionType.Add:
                        value = first + second;

                        return true;
                    case BinaryExpressionType.Subtract:
                        value = first - second;

                        return true;
                    case BinaryExpressionType.Multiply:
                        value = first * second;

                        return true;
                }
            }
            catch (OverflowException)
            {
                value = 0;

                return false;
            }
        }

        value = 0;

        return false;
    }

    private static bool TryStringExpressionValue(
        ScalarExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        out string value)
    {
        if (expression is VariableReference variable
            && variables.TryGetValue(variable.Name, out var variableValue))
        {
            return TryStringLiteralValue(variableValue, out value);
        }

        if (TryStringLiteralValue(expression, out value))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryStringExpressionValue(parenthesis.Expression, variables, out value);
        }

        value = string.Empty;

        return false;
    }

    private static bool TryStringLiteralValue(ScalarExpression expression, out string value)
    {
        if (expression is StringLiteral literal)
        {
            value = literal.Value;

            return true;
        }

        value = string.Empty;

        return false;
    }

    private static bool TryResolveLiteral(
        ScalarExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        out ScalarExpression value)
    {
        if (expression is VariableReference variable
            && variables.TryGetValue(variable.Name, out value!))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryResolveLiteral(parenthesis.Expression, variables, out value);
        }

        if (TryLiteralValue(expression, out _))
        {
            value = expression;

            return true;
        }

        value = null!;

        return false;
    }

    private static bool? Negate(bool? value) => value is null ? null : !value.Value;
}
