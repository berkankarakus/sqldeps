using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Collects literal variable values that are safe to use during one batch analysis.</summary>
internal sealed class ConstantVariableEnvironment : TSqlFragmentVisitor
{
    private readonly ConstantVariableValueStore _valueStore = new();
    private readonly ConstantVariableIgnoredBranchStore _ignoredBranchStatements = new();

    public static ConstantVariableEnvironmentResult Find(TSqlBatch batch)
    {
        ConstantVariableEnvironment environment = new();
        batch.Accept(environment);

        return new(
            environment._valueStore.Values,
            environment._valueStore.ResolvedTemporalVariables
        );
    }

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
                _valueStore.MarkTemporal(declaration.VariableName.Value);
            }

            if (declaration.Value is null)
            {
                continue;
            }

            if (!TryLiteral(declaration.Value, out var value))
            {
                _valueStore.Invalidate(declaration.VariableName.Value);

                continue;
            }

            _valueStore.Record(declaration.VariableName.Value, value);
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
                _valueStore.Invalidate(variableName);
            }

            base.Visit(node);

            return;
        }

        _valueStore.Record(variableName, value);
        base.Visit(node);
    }

    public override void Visit(IfStatement node)
    {
        var before = CaptureState();

        var condition = ConstantExpressionEvaluator.Evaluate(
            node.Predicate,
            _valueStore.KnownValues,
            _valueStore.TemporalVariableNames);

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

        ConstantVariableBranchAssignmentFinder finder = new();
        branch.Accept(finder);
        _ignoredBranchStatements.AddRange(finder.Statements);
    }

    public override void Visit(WhileStatement node)
    {
        var before = CaptureState();

        var condition = ConstantExpressionEvaluator.Evaluate(
            node.Predicate,
            _valueStore.KnownValues,
            _valueStore.TemporalVariableNames);

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
        return _valueStore.CaptureState();
    }

    private void RestoreState(ConstantVariableEnvironmentState state)
    {
        _valueStore.RestoreState(state);
    }

    private static ConstantVariableEnvironmentState MergeStates(
        ConstantVariableEnvironmentState first,
        ConstantVariableEnvironmentState second
    )
    {
        return ConstantVariableEnvironmentState.Merge(first, second);
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
        ConstantVariableLiteralResolver resolver = new(
            _valueStore.KnownValues,
            _valueStore.TemporalVariableNames
        );

        return resolver.TryLiteral(expression, out value);
    }

}

internal sealed record ConstantVariableEnvironmentResult(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables
);
