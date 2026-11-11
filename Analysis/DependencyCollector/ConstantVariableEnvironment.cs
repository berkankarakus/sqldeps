using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Collects literal variable values that are safe to use during one batch analysis.</summary>
internal sealed class ConstantVariableEnvironment : TSqlFragmentVisitor
{
    private readonly Dictionary<string, ScalarExpression> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _temporalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalidVariables = new(StringComparer.OrdinalIgnoreCase);
    private int _conditionalDepth;

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

            if (_conditionalDepth > 0 || !TryLiteral(declaration.Value, out var value))
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
        if (variableName is null || _conditionalDepth > 0 || !TryLiteral(node.Expression, out var value))
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
        VisitConditional(node.ThenStatement);

        if (node.ElseStatement is not null)
        {
            VisitConditional(node.ElseStatement);
        }
    }

    public override void Visit(WhileStatement node)
    {
        VisitConditional(node.Statement);
    }

    public override void Visit(TryCatchStatement node)
    {
        VisitConditional(node.TryStatements);
        VisitConditional(node.CatchStatements);
    }

    private void VisitConditional(TSqlFragment statement)
    {
        _conditionalDepth++;
        statement.Accept(this);
        _conditionalDepth--;
    }

    private void Record(string variableName, ScalarExpression value)
    {
        if (_invalidVariables.Contains(variableName) || _values.ContainsKey(variableName))
        {
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
}

internal sealed record ConstantVariableEnvironmentResult(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables
);
