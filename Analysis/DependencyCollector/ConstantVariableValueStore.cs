using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Owns mutable constant-variable state during one batch analysis.</summary>
internal sealed class ConstantVariableValueStore
{
    private readonly Dictionary<string, ScalarExpression> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _temporalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalidVariables = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, ScalarExpression> KnownValues
    {
        get
        {
            return _values;
        }
    }

    internal IReadOnlySet<string> TemporalVariableNames
    {
        get
        {
            return _temporalVariables;
        }
    }

    internal IReadOnlyDictionary<string, ScalarExpression> Values
    {
        get
        {
            return _values
                .Where(pair => !_invalidVariables.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal IReadOnlySet<string> ResolvedTemporalVariables
    {
        get
        {
            return _temporalVariables
                .Where(variable => _values.ContainsKey(variable) && !_invalidVariables.Contains(variable))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal void MarkTemporal(string variableName)
    {
        _temporalVariables.Add(variableName);
    }

    internal void Record(string variableName, ScalarExpression value)
    {
        if (_invalidVariables.Contains(variableName))
        {
            Invalidate(variableName);

            return;
        }

        if (_values.TryGetValue(variableName, out var existingValue))
        {
            if (ConstantVariableLiteralComparer.AreEqual(existingValue, value))
            {
                return;
            }

            Invalidate(variableName);

            return;
        }

        _values[variableName] = value;
    }

    internal void Invalidate(string variableName)
    {
        _invalidVariables.Add(variableName);
        _values.Remove(variableName);
    }

    internal ConstantVariableEnvironmentState CaptureState()
    {
        return ConstantVariableEnvironmentState.Capture(
            _values,
            _temporalVariables,
            _invalidVariables
        );
    }

    internal void RestoreState(ConstantVariableEnvironmentState state)
    {
        state.RestoreTo(
            _values,
            _temporalVariables,
            _invalidVariables
        );
    }
}
