using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Resolves literal and same-result CASE expressions against the current variable state.</summary>
internal sealed class ConstantVariableLiteralResolver(
    IReadOnlyDictionary<string, ScalarExpression> values,
    IReadOnlySet<string> temporalVariables
)
{
    internal bool TryLiteral(ScalarExpression? expression, out ScalarExpression value)
    {
        if (expression is not null
            && ConstantExpressionEvaluator.TryResolveLiteral(expression, values, out value))
        {
            return true;
        }

        if (expression is not null
            && ConstantExpressionEvaluator.TryCreateNumericLiteral(
                expression,
                values,
                out value))
        {
            return true;
        }

        if (expression is not null
            && ConstantExpressionEvaluator.TryCreateStringLiteral(
                expression,
                values,
                out value))
        {
            return true;
        }

        if (expression is CaseExpression caseExpression
            && TrySameCaseLiteral(caseExpression, out value))
        {
            return true;
        }

        value = null!;

        return false;
    }

    private bool TrySameCaseLiteral(CaseExpression expression, out ScalarExpression value)
    {
        value = null!;

        if (expression.ElseExpression is null
            || !TryLiteral(expression.ElseExpression, out var elseValue))
        {
            return false;
        }

        if (expression is SearchedCaseExpression searchedCase)
        {
            return TrySearchedCaseLiteral(
                searchedCase.WhenClauses,
                elseValue,
                out value
            );
        }

        if (expression is not SimpleCaseExpression simpleCase)
        {
            return false;
        }

        if (TryKnownSimpleCaseLiteral(simpleCase, out value))
        {
            return true;
        }

        IEnumerable<WhenClause> whenClauses = simpleCase.WhenClauses;

        foreach (var whenClause in whenClauses)
        {
            if (!TryLiteral(whenClause.ThenExpression, out var thenValue)
                || !ConstantVariableEnvironmentState.SameLiteral(elseValue, thenValue))
            {
                return false;
            }
        }

        value = elseValue;

        return whenClauses.Any();
    }

    private bool TryKnownSimpleCaseLiteral(
        SimpleCaseExpression expression,
        out ScalarExpression value
    )
    {
        value = null!;

        if (!ConstantExpressionEvaluator.TryResolveLiteral(
                expression.InputExpression,
                values,
                out var inputValue))
        {
            return false;
        }

        foreach (var whenClause in expression.WhenClauses)
        {
            if (!ConstantExpressionEvaluator.TryResolveLiteral(
                    whenClause.WhenExpression,
                    values,
                    out var whenValue))
            {
                return false;
            }

            if (inputValue is NullLiteral || whenValue is NullLiteral)
            {
                continue;
            }

            if (!ConstantVariableEnvironmentState.SameLiteral(inputValue, whenValue))
            {
                continue;
            }

            return TryLiteral(whenClause.ThenExpression, out value);
        }

        if (expression.ElseExpression is null
            || !TryLiteral(expression.ElseExpression, out value))
        {
            value = null!;

            return false;
        }

        return true;
    }

    private bool TrySearchedCaseLiteral(
        IList<SearchedWhenClause> whenClauses,
        ScalarExpression elseValue,
        out ScalarExpression value
    )
    {
        value = null!;
        ScalarExpression? candidate = null;

        foreach (var whenClause in whenClauses)
        {
            var condition = ConstantExpressionEvaluator.Evaluate(
                whenClause.WhenExpression,
                values,
                temporalVariables);

            if (condition == false)
            {
                continue;
            }

            if (!TryLiteral(whenClause.ThenExpression, out var thenValue))
            {
                return false;
            }

            if (candidate is null)
            {
                candidate = thenValue;
            }

            else if (!ConstantVariableEnvironmentState.SameLiteral(candidate, thenValue))
            {
                return false;
            }

            if (condition == true)
            {
                value = candidate;

                return true;
            }
        }

        if (candidate is null)
        {
            value = elseValue;

            return true;
        }

        if (!ConstantVariableEnvironmentState.SameLiteral(candidate, elseValue))
        {
            return false;
        }

        value = candidate;

        return true;
    }
}
