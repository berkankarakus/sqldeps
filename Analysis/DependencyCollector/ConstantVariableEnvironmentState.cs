using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Immutable branch state used while resolving constant variables.</summary>
internal sealed record ConstantVariableEnvironmentState(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables,
    IReadOnlySet<string> InvalidVariables
)
{
    internal static ConstantVariableEnvironmentState Capture(
        IReadOnlyDictionary<string, ScalarExpression> values,
        IReadOnlySet<string> temporalVariables,
        IReadOnlySet<string> invalidVariables
    )
    {
        return new(
            new Dictionary<string, ScalarExpression>(values, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(temporalVariables, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(invalidVariables, StringComparer.OrdinalIgnoreCase)
        );
    }

    internal void RestoreTo(
        IDictionary<string, ScalarExpression> values,
        ISet<string> temporalVariables,
        ISet<string> invalidVariables
    )
    {
        values.Clear();

        foreach (var pair in Values)
        {
            values[pair.Key] = pair.Value;
        }

        temporalVariables.Clear();
        temporalVariables.UnionWith(TemporalVariables);

        invalidVariables.Clear();
        invalidVariables.UnionWith(InvalidVariables);
    }

    internal static ConstantVariableEnvironmentState Merge(
        ConstantVariableEnvironmentState first,
        ConstantVariableEnvironmentState second
    )
    {
        Dictionary<string, ScalarExpression> values = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> temporalVariables = new(first.TemporalVariables, StringComparer.OrdinalIgnoreCase);
        HashSet<string> invalidVariables = new(first.InvalidVariables, StringComparer.OrdinalIgnoreCase);

        foreach (var variable in first.Values.Keys.Intersect(second.Values.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (ConstantVariableLiteralComparer.AreEqual(first.Values[variable], second.Values[variable]))
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
}
