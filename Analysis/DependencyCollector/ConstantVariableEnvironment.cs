using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Collects literal variable values that are safe to use during one batch analysis.</summary>
internal sealed class ConstantVariableEnvironment : TSqlFragmentVisitor
{
    private readonly Dictionary<string, ScalarExpression> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _temporalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalidVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TSqlFragment> _ignoredBranchStatements = [];

    public static ConstantVariableEnvironmentResult Find(TSqlBatch batch)
    {
        ConstantVariableEnvironment environment = new();
        batch.Accept(environment);

        return new(environment.Values, environment.TemporalVariables);
    }

    private IReadOnlyDictionary<string, ScalarExpression> Values => _values
        .Where(pair => !_invalidVariables.Contains(pair.Key))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private IReadOnlySet<string> TemporalVariables => _temporalVariables
        .Where(variable => _values.ContainsKey(variable) && !_invalidVariables.Contains(variable))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public override void Visit(DeclareVariableStatement node)
    {
        if (_ignoredBranchStatements.Remove(node))
        {
            return;
        }

        foreach (var declaration in node.Declarations)
        {
            if (IsTemporalType(declaration.DataType))
            {
                _temporalVariables.Add(declaration.VariableName.Value);
            }

            if (declaration.Value is null)
            {
                continue;
            }

            if (!TryLiteral(declaration.Value, out var value))
            {
                Invalidate(declaration.VariableName.Value);

                continue;
            }

            Record(declaration.VariableName.Value, value);
        }

        base.Visit(node);
    }

    public override void Visit(SetVariableStatement node)
    {
        if (_ignoredBranchStatements.Remove(node))
        {
            return;
        }

        string? variableName = node.Variable?.Name;
        if (variableName is null || !TryLiteral(node.Expression, out var value))
        {
            if (variableName is not null)
            {
                Invalidate(variableName);
            }

            base.Visit(node);

            return;
        }

        Record(variableName, value);
        base.Visit(node);
    }

    public override void Visit(IfStatement node)
    {
        var before = CaptureState();

        var condition = ConstantExpressionEvaluator.Evaluate(
            node.Predicate,
            _values,
            _temporalVariables);

        if (condition == true)
        {
            IgnoreBranchAssignments(node.ElseStatement);
            RestoreState(StateAfter(node.ThenStatement, before));

            return;
        }

        if (condition == false)
        {
            IgnoreBranchAssignments(node.ThenStatement);
            var falseState = node.ElseStatement is null
                ? before
                : StateAfter(node.ElseStatement, before);
            RestoreState(falseState);

            return;
        }

        var thenState = StateAfter(node.ThenStatement, before);
        var elseState = node.ElseStatement is null
            ? before
            : StateAfter(node.ElseStatement, before);

        RestoreState(MergeStates(thenState, elseState));
    }

    private void IgnoreBranchAssignments(TSqlFragment? branch)
    {
        if (branch is null)
        {
            return;
        }

        BranchAssignmentFinder finder = new();
        branch.Accept(finder);
        _ignoredBranchStatements.UnionWith(finder.Statements);
    }

    public override void Visit(WhileStatement node)
    {
        var before = CaptureState();

        var condition = ConstantExpressionEvaluator.Evaluate(
            node.Predicate,
            _values,
            _temporalVariables);

        if (condition == false)
        {
            IgnoreBranchAssignments(node.Statement);
            RestoreState(before);

            return;
        }

        var body = StateAfter(node.Statement, before);

        RestoreState(MergeStates(before, body));
    }

    public override void Visit(TryCatchStatement node)
    {
        var before = CaptureState();
        var tryState = StateAfter(node.TryStatements, before);
        var catchState = StateAfter(node.CatchStatements, before);

        RestoreState(MergeStates(tryState, catchState));
    }

    private ConstantVariableEnvironmentState StateAfter(
        TSqlFragment statement,
        ConstantVariableEnvironmentState before
    )
    {
        RestoreState(before);
        statement.Accept(this);

        return CaptureState();
    }

    private ConstantVariableEnvironmentState CaptureState()
    {
        return ConstantVariableEnvironmentState.Capture(
            _values,
            _temporalVariables,
            _invalidVariables
        );
    }

    private void RestoreState(ConstantVariableEnvironmentState state)
    {
        state.RestoreTo(
            _values,
            _temporalVariables,
            _invalidVariables
        );
    }

    private static ConstantVariableEnvironmentState MergeStates(
        ConstantVariableEnvironmentState first,
        ConstantVariableEnvironmentState second
    )
    {
        return ConstantVariableEnvironmentState.Merge(first, second);
    }

    private static bool SameLiteral(ScalarExpression first, ScalarExpression second)
    {
        return ConstantVariableEnvironmentState.SameLiteral(first, second);
    }

    private void Record(string variableName, ScalarExpression value)
    {
        if (_invalidVariables.Contains(variableName))
        {
            Invalidate(variableName);

            return;
        }

        if (_values.TryGetValue(variableName, out var existingValue))
        {
            if (SameLiteral(existingValue, value))
            {
                return;
            }

            Invalidate(variableName);

            return;
        }

        _values[variableName] = value;
    }

    private void Invalidate(string variableName)
    {
        _invalidVariables.Add(variableName);
        _values.Remove(variableName);
    }

    private static bool IsTemporalType(DataTypeReference dataType)
    {
        if (dataType is not SqlDataTypeReference sqlDataType)
        {
            return false;
        }

        return sqlDataType.SqlDataTypeOption.ToString() is
            "Date" or "DateTime" or "DateTime2" or "DateTimeOffset" or "SmallDateTime" or "Time";
    }

    private bool TryLiteral(ScalarExpression? expression, out ScalarExpression value)
    {
        if (expression is not null
            && ConstantExpressionEvaluator.TryResolveLiteral(expression, _values, out value))
        {
            return true;
        }

        if (expression is not null
            && ConstantExpressionEvaluator.TryCreateNumericLiteral(
                expression,
                _values,
                out value))
        {
            return true;
        }

        if (expression is not null
            && ConstantExpressionEvaluator.TryCreateStringLiteral(
                expression,
                _values,
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
                || !SameLiteral(elseValue, thenValue))
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
                _values,
                out var inputValue))
        {
            return false;
        }

        foreach (var whenClause in expression.WhenClauses)
        {
            if (!ConstantExpressionEvaluator.TryResolveLiteral(
                    whenClause.WhenExpression,
                    _values,
                    out var whenValue))
            {
                return false;
            }

            if (inputValue is NullLiteral || whenValue is NullLiteral)
            {
                continue;
            }

            if (!SameLiteral(inputValue, whenValue))
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
                _values,
                _temporalVariables);

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
            else if (!SameLiteral(candidate, thenValue))
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

        if (!SameLiteral(candidate, elseValue))
        {
            return false;
        }

        value = candidate;

        return true;
    }

    private sealed class BranchAssignmentFinder : TSqlFragmentVisitor
    {
        public List<TSqlFragment> Statements { get; } = [];

        public override void Visit(DeclareVariableStatement node)
        {
            Statements.Add(node);
        }

        public override void Visit(SetVariableStatement node)
        {
            Statements.Add(node);
        }
    }

}

internal sealed record ConstantVariableEnvironmentResult(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables
);
