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
            _ => null,
        };
    }

    private static bool? EvaluateBinary(
        BooleanBinaryExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        var first = Evaluate(expression.FirstExpression, variables);
        var second = Evaluate(expression.SecondExpression, variables);
        if (first is null || second is null)
        {
            return null;
        }

        return expression.BinaryExpressionType == BooleanBinaryExpressionType.And
            ? first.Value && second.Value
            : first.Value || second.Value;
    }

    private static bool? EvaluateIsNull(
        BooleanIsNullExpression expression,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (!TryResolveLiteral(expression.Expression, variables, out _))
        {
            return null;
        }

        return expression.IsNot;
    }

    private static bool? EvaluateComparisonWithVariables(
        BooleanComparisonExpression comparison,
        IReadOnlyDictionary<string, ScalarExpression> variables)
    {
        if (!TryResolveLiteral(comparison.FirstExpression, variables, out var first)
            || !TryResolveLiteral(comparison.SecondExpression, variables, out var second)
            || !TryNumericLiteralValue(first, out var firstValue)
            || !TryNumericLiteralValue(second, out var secondValue))
        {
            return null;
        }

        return CompareNumericValues(comparison.ComparisonType, firstValue, secondValue);
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
