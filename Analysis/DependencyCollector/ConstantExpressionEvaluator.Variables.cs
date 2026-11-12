using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class ConstantExpressionEvaluator
{
    public static bool? Evaluate(
        BooleanExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables = null)
    {
        var result = Evaluate(expression);
        if (result is not null)
        {
            return result;
        }

        return EvaluateKnownVariables(expression, variables, temporalVariables);
    }

    private static bool? EvaluateKnownVariables(
        BooleanExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables)
    {
        return expression switch
        {
            BooleanParenthesisExpression parenthesis => Evaluate(parenthesis.Expression, variables, temporalVariables),
            BooleanNotExpression not => Negate(Evaluate(not.Expression, variables, temporalVariables)),
            BooleanBinaryExpression binary => EvaluateBinary(binary, variables, temporalVariables),
            BooleanIsNullExpression isNull => EvaluateIsNull(isNull, variables),
            BooleanComparisonExpression comparison => EvaluateComparisonWithVariables(comparison, variables, temporalVariables),
            BooleanTernaryExpression ternary => EvaluateBetweenWithVariables(ternary, variables, temporalVariables),
            InPredicate inPredicate => EvaluateInWithVariables(inPredicate, variables, temporalVariables),
            _ => null,
        };
    }

    private static bool? EvaluateBinary(
        BooleanBinaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables)
    {
        var first = Evaluate(expression.FirstExpression, variables, temporalVariables);
        var second = Evaluate(expression.SecondExpression, variables, temporalVariables);

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
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables)
    {
        if (TryNumericExpressionValue(comparison.FirstExpression, variables, out var firstNumber)
            && TryNumericExpressionValue(comparison.SecondExpression, variables, out var secondNumber))
        {
            return CompareNumericValues(comparison.ComparisonType, firstNumber, secondNumber);
        }

        if (!TryTemporalExpressionValue(comparison.FirstExpression, variables, temporalVariables, out var firstTemporal)
            || !TryTemporalExpressionValue(comparison.SecondExpression, variables, temporalVariables, out var secondTemporal))
        {
            if (TryStringExpressionValue(comparison.FirstExpression, variables, out var firstText)
                && TryStringExpressionValue(comparison.SecondExpression, variables, out var secondText))
            {
                return CompareStringValues(comparison.ComparisonType, firstText, secondText);
            }

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
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables)
    {
        if (TryNumericBetween(expression, variables, out var numericResult))
        {
            return numericResult;
        }

        if (TryTemporalBetween(expression, variables, temporalVariables, out var temporalResult))
        {
            return temporalResult;
        }

        return null;
    }

    private static bool TryNumericBetween(
        BooleanTernaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        out bool result)
    {
        if (!TryNumericExpressionValue(expression.FirstExpression, variables, out var value))
        {
            result = false;

            return false;
        }

        if (!TryNumericExpressionValue(expression.SecondExpression, variables, out var lower))
        {
            result = false;

            return false;
        }

        if (!TryNumericExpressionValue(expression.ThirdExpression, variables, out var upper))
        {
            result = false;

            return false;
        }

        var between = value >= lower && value <= upper;
        result = expression.TernaryExpressionType == BooleanTernaryExpressionType.Between
            ? between
            : !between;

        return true;
    }

    private static bool TryTemporalBetween(
        BooleanTernaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables,
        out bool result)
    {
        if (!TryTemporalExpressionValue(expression.FirstExpression, variables, temporalVariables, out var value))
        {
            result = false;

            return false;
        }

        if (!TryTemporalExpressionValue(expression.SecondExpression, variables, temporalVariables, out var lower))
        {
            result = false;

            return false;
        }

        if (!TryTemporalExpressionValue(expression.ThirdExpression, variables, temporalVariables, out var upper))
        {
            result = false;

            return false;
        }

        var lowerMatch = CompareTemporalValues(
            BooleanComparisonType.GreaterThanOrEqualTo,
            value,
            lower);
        var upperMatch = CompareTemporalValues(
            BooleanComparisonType.LessThanOrEqualTo,
            value,
            upper);

        if (lowerMatch is null || upperMatch is null)
        {
            result = false;

            return false;
        }

        var between = lowerMatch.Value && upperMatch.Value;
        result = expression.TernaryExpressionType == BooleanTernaryExpressionType.Between
            ? between
            : !between;

        return true;
    }

    private static bool? EvaluateInWithVariables(
        InPredicate expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables)
    {
        if (expression.Subquery is not null || expression.Values.Count == 0)
        {
            return null;
        }

        if (TryNumericExpressionValue(expression.Expression, variables, out var numericCandidate))
        {
            return EvaluateNumericIn(expression, variables, numericCandidate);
        }

        if (TryTemporalExpressionValue(expression.Expression, variables, temporalVariables, out var temporalCandidate))
        {
            return EvaluateTemporalIn(expression, variables, temporalVariables, temporalCandidate);
        }

        if (!TryStringExpressionValue(expression.Expression, variables, out var stringCandidate))
        {
            return null;
        }

        return EvaluateStringIn(expression, variables, stringCandidate);
    }

    private static bool? EvaluateNumericIn(
        InPredicate expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        decimal candidate)
    {
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

    private static bool? EvaluateTemporalIn(
        InPredicate expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables,
        TemporalValue candidate)
    {
        var contains = false;

        foreach (var item in expression.Values)
        {
            if (!TryTemporalExpressionValue(item, variables, temporalVariables, out var value))
            {
                return null;
            }

            var matches = CompareTemporalValues(BooleanComparisonType.Equals, candidate, value);
            if (matches is null)
            {
                return null;
            }

            contains |= matches.Value;
        }

        return expression.NotDefined ? !contains : contains;
    }

    private static bool? EvaluateStringIn(
        InPredicate expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        string candidate)
    {
        List<string> values = [];

        foreach (var item in expression.Values)
        {
            if (!TryStringExpressionValue(item, variables, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        var contains = values.Contains(candidate, StringComparer.Ordinal);

        return expression.NotDefined ? !contains : contains;
    }

    private static bool TryTemporalExpressionValue(
        ScalarExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables,
        IReadOnlySet<string>? temporalVariables,
        out TemporalValue value)
    {
        if (expression is VariableReference variable
            && variables.TryGetValue(variable.Name, out var variableValue))
        {
            if (temporalVariables is not null && !temporalVariables.Contains(variable.Name))
            {
                value = default;

                return false;
            }

            return TryTemporalLiteralValue(variableValue, out value);
        }

        if (TryTemporalLiteralValue(expression, out value))
        {
            return true;
        }

        if (expression is ParenthesisExpression parenthesis)
        {
            return TryTemporalExpressionValue(parenthesis.Expression, variables, temporalVariables, out value);
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

    internal static bool TryResolveLiteral(
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

        if (expression is NullLiteral
            || TryLiteralValue(expression, out _))
        {
            value = expression;

            return true;
        }

        value = null!;

        return false;
    }

    private static bool? Negate(bool? value) => value is null ? null : !value.Value;
}
