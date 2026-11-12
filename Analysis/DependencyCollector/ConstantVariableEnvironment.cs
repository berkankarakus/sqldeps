using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Collects literal variable values that are safe to use during one batch analysis.</summary>
internal sealed class ConstantVariableEnvironment : TSqlFragmentVisitor
{
    private readonly Dictionary<string, ScalarExpression> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _temporalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalidVariables = new(StringComparer.OrdinalIgnoreCase);

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
        var thenState = StateAfter(node.ThenStatement, before);
        var elseState = node.ElseStatement is null
            ? before
            : StateAfter(node.ElseStatement, before);

        RestoreState(MergeStates(thenState, elseState));
    }

    public override void Visit(WhileStatement node)
    {
        var before = CaptureState();
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

    private EnvironmentState StateAfter(TSqlFragment statement, EnvironmentState before)
    {
        RestoreState(before);
        statement.Accept(this);

        return CaptureState();
    }

    private EnvironmentState CaptureState()
    {
        return new(
            new Dictionary<string, ScalarExpression>(_values, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_temporalVariables, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_invalidVariables, StringComparer.OrdinalIgnoreCase));
    }

    private void RestoreState(EnvironmentState state)
    {
        _values.Clear();
        foreach (var pair in state.Values)
        {
            _values[pair.Key] = pair.Value;
        }

        _temporalVariables.Clear();
        _temporalVariables.UnionWith(state.TemporalVariables);

        _invalidVariables.Clear();
        _invalidVariables.UnionWith(state.InvalidVariables);
    }

    private static EnvironmentState MergeStates(
        EnvironmentState first,
        EnvironmentState second)
    {
        Dictionary<string, ScalarExpression> values = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> temporalVariables = new(first.TemporalVariables, StringComparer.OrdinalIgnoreCase);
        HashSet<string> invalidVariables = new(first.InvalidVariables, StringComparer.OrdinalIgnoreCase);

        foreach (var variable in first.Values.Keys.Intersect(second.Values.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (SameLiteral(first.Values[variable], second.Values[variable]))
            {
                values[variable] = first.Values[variable];
            }
            else
            {
                invalidVariables.Add(variable);
            }
        }

        foreach (var variable in first.Values.Keys.Union(second.Values.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!values.ContainsKey(variable))
            {
                invalidVariables.Add(variable);
            }
        }

        return new(values, temporalVariables, invalidVariables);
    }

    private static bool SameLiteral(ScalarExpression first, ScalarExpression second)
    {
        if (first is NullLiteral || second is NullLiteral)
        {
            return first is NullLiteral && second is NullLiteral;
        }

        if (first.GetType() != second.GetType())
        {
            return false;
        }

        return ConstantExpressionEvaluator.TryLiteralValue(first, out var firstValue)
            && ConstantExpressionEvaluator.TryLiteralValue(second, out var secondValue)
            && string.Equals(firstValue, secondValue, StringComparison.Ordinal);
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

    private static bool TryLiteral(ScalarExpression? expression, out ScalarExpression value)
    {
        if (expression is NullLiteral
            || expression is not null && ConstantExpressionEvaluator.TryLiteralValue(expression, out _))
        {
            value = expression;

            return true;
        }

        value = null!;

        return false;
    }

    private sealed record EnvironmentState(
        IReadOnlyDictionary<string, ScalarExpression> Values,
        IReadOnlySet<string> TemporalVariables,
        IReadOnlySet<string> InvalidVariables
    );
}

internal sealed record ConstantVariableEnvironmentResult(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables
);
