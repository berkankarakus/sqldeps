using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Collects literal variable values that are safe to use during one batch analysis.</summary>
internal sealed class ConstantVariableEnvironment : TSqlFragmentVisitor
{
    private readonly bool _captureExecutionStates;
    private readonly ConstantVariableValueStore _valueStore = new();
    private readonly ConstantVariableIgnoredBranchStore _ignoredBranchStatements = new();
    private readonly HashSet<string> _stringVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TSqlFragment, ConstantVariableExecutionState> _executionStates = [];
    private readonly Stack<List<ConstantVariableEnvironmentState>> _tryAssignmentStates = [];
    private bool _isCatchPath;
    private bool _isLoopPath;

    private ConstantVariableEnvironment(bool captureExecutionStates)
    {
        _captureExecutionStates = captureExecutionStates;
    }

    public static ConstantVariableEnvironmentResult Find(TSqlBatch batch)
    {
        ConstantVariableEnvironment environment = new(captureExecutionStates: false);
        batch.Accept(environment);
        ConstantVariableEnvironment executionEnvironment = new(captureExecutionStates: true);
        batch.Accept(executionEnvironment);

        return new(
            environment._valueStore.Values,
            environment._valueStore.ResolvedTemporalVariables,
            executionEnvironment._executionStates
        );
    }

    public override void Visit(ExecutableProcedureReference node)
    {
        if (_captureExecutionStates)
        {
            CaptureExecutionState(node);
        }

        base.Visit(node);
    }

    public override void Visit(ExecutableStringList node)
    {
        if (_captureExecutionStates)
        {
            CaptureExecutionState(node);
        }

        base.Visit(node);
    }

    public override void Visit(DeclareVariableStatement node)
    {
        VisitDeclareVariableStatement(node);
        CaptureTryAssignmentState();
    }

    private void VisitDeclareVariableStatement(DeclareVariableStatement node)
    {
        if (_ignoredBranchStatements.Remove(node))
        {
            return;
        }

        foreach (var declaration in node.Declarations)
        {
            if (IsStringType(declaration.DataType))
            {
                _stringVariables.Add(NormalizeVariableName(declaration.VariableName.Value));
            }

            if (IsTemporalType(declaration.DataType))
            {
                _valueStore.MarkTemporal(declaration.VariableName.Value);
            }

            if (declaration.Value is null)
            {
                _valueStore.ClearDynamicSql(declaration.VariableName.Value);
                continue;
            }

            UpdateDynamicSqlProvenance(declaration.VariableName.Value, declaration.Value);

            if (!TryLiteral(declaration.Value, out var value))
            {
                _valueStore.Invalidate(declaration.VariableName.Value);

                continue;
            }

            _valueStore.Record(declaration.VariableName.Value, value);
        }

        base.Visit(node);
    }

    public override void Visit(ProcedureParameter node)
    {
        if (IsStringType(node.DataType))
        {
            _stringVariables.Add(NormalizeVariableName(node.VariableName.Value));
        }

        base.Visit(node);
    }

    public override void Visit(SetVariableStatement node)
    {
        VisitSetVariableStatement(node);
        CaptureTryAssignmentState();
    }

    private void VisitSetVariableStatement(SetVariableStatement node)
    {
        if (_ignoredBranchStatements.Remove(node))
        {
            return;
        }

        string? variableName = node.Variable?.Name;
        string normalizedVariableName = NormalizeVariableName(variableName ?? "");
        bool wasDynamicSql = variableName is not null
            && _valueStore.DynamicSqlVariables.Contains(normalizedVariableName);

        if (variableName is not null)
        {
            UpdateDynamicSqlProvenance(variableName, node.Expression);
        }

        if (variableName is null || !TryLiteral(node.Expression, out var value))
        {
            if (variableName is not null)
            {
                _valueStore.Invalidate(variableName);
            }

            base.Visit(node);

            return;
        }

        if (_captureExecutionStates
            && (wasDynamicSql || _stringVariables.Contains(normalizedVariableName) || _isCatchPath))
        {
            _valueStore.Assign(variableName, value);
        }

        else
        {
            _valueStore.Record(variableName, value);
        }

        base.Visit(node);
    }

    public override void Visit(SelectSetVariable node)
    {
        VisitSelectSetVariable(node);
        CaptureTryAssignmentState();
    }

    private void VisitSelectSetVariable(SelectSetVariable node)
    {
        if (_ignoredBranchStatements.Remove(node))
        {
            return;
        }

        string? variableName = node.Variable?.Name;
        if (variableName is not null)
        {
            UpdateDynamicSqlProvenance(variableName, node.Expression);
            _valueStore.Invalidate(variableName);
        }

        base.Visit(node);
    }

    public override void ExplicitVisit(IfStatement node)
    {
        if (_captureExecutionStates)
        {
            Visit(node);

            return;
        }

        base.ExplicitVisit(node);
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
        if (_captureExecutionStates || branch is null)
        {
            return;
        }

        ConstantVariableBranchAssignmentFinder finder = new();
        branch.Accept(finder);
        _ignoredBranchStatements.AddRange(finder.Statements);
    }

    public override void ExplicitVisit(WhileStatement node)
    {
        if (_captureExecutionStates)
        {
            Visit(node);

            return;
        }

        base.ExplicitVisit(node);
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

        if (!_captureExecutionStates)
        {
            var bodyState = StateAfter(node.Statement, before);
            RestoreState(MergeStates(before, bodyState));

            return;
        }

        bool wasLoopPath = _isLoopPath;
        _isLoopPath = true;

        try
        {
            ConstantVariableEnvironmentState loopState = before;

            while (true)
            {
                var iterationState = StateAfter(node.Statement, loopState);
                var nextLoopState = MergeStates(loopState, iterationState);

                if (HasSameValues(loopState, nextLoopState))
                {
                    RestoreState(nextLoopState);

                    break;
                }

                loopState = nextLoopState;
            }
        }

        finally
        {
            _isLoopPath = wasLoopPath;
        }
    }

    public override void ExplicitVisit(TryCatchStatement node)
    {
        if (_captureExecutionStates)
        {
            Visit(node);

            return;
        }

        base.ExplicitVisit(node);
    }

    public override void Visit(TryCatchStatement node)
    {
        var before = CaptureState();
        ConstantVariableEnvironmentState tryState;
        ConstantVariableEnvironmentState catchEntryState;
        List<ConstantVariableEnvironmentState> assignmentStates = [before];

        if (_captureExecutionStates)
        {
            _tryAssignmentStates.Push(assignmentStates);
        }

        try
        {
            tryState = StateAfter(node.TryStatements, before);
        }

        finally
        {
            if (_captureExecutionStates)
            {
                _tryAssignmentStates.Pop();
            }
        }

        catchEntryState = before;

        foreach (var state in assignmentStates)
        {
            catchEntryState = MergeStates(catchEntryState, state);
        }

        var catchState = StateAfterCatch(node.CatchStatements, catchEntryState);

        RestoreState(MergeStates(tryState, catchState));
    }

    private void CaptureTryAssignmentState()
    {
        foreach (var states in _tryAssignmentStates)
        {
            states[0] = MergeStates(states[0], CaptureState());
        }
    }

    private ConstantVariableEnvironmentState StateAfterCatch(
        StatementList statements,
        ConstantVariableEnvironmentState before
    )
    {
        if (!_captureExecutionStates)
        {
            return StateAfter(statements, before);
        }

        bool wasCatchPath = _isCatchPath;
        _isCatchPath = true;

        try
        {
            return StateAfter(statements, before);
        }

        finally
        {
            _isCatchPath = wasCatchPath;
        }
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

    private static bool HasSameValues(
        ConstantVariableEnvironmentState first,
        ConstantVariableEnvironmentState second
    )
    {
        if (first.Values.Count != second.Values.Count
            || !first.Values.Keys.All(second.Values.ContainsKey)
            || !first.InvalidVariables.SetEquals(second.InvalidVariables)
            || !first.DynamicSqlVariables.SetEquals(second.DynamicSqlVariables)
            || !first.TemporalVariables.SetEquals(second.TemporalVariables))
        {
            return false;
        }

        return first.Values.All(pair =>
            ConstantVariableLiteralComparer.AreEqual(pair.Value, second.Values[pair.Key]));
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

    private void UpdateDynamicSqlProvenance(string variableName, ScalarExpression? expression)
    {
        string normalizedVariableName = NormalizeVariableName(variableName);

        if (!TryLiteral(expression, out _)
            && (IsDynamicSqlConcatenation(expression) || ReferencesDynamicSqlVariable(expression)))
        {
            _valueStore.MarkDynamicSql(normalizedVariableName);

            return;
        }

        _valueStore.ClearDynamicSql(normalizedVariableName);
    }

    private bool ReferencesDynamicSqlVariable(ScalarExpression? expression)
    {
        if (expression is null || _valueStore.DynamicSqlVariables.Count == 0)
        {
            return false;
        }

        DynamicSqlVariableReferenceFinder finder = new();
        expression.Accept(finder);

        return finder.VariableNames.Any(_valueStore.DynamicSqlVariables.Contains);
    }

    private bool IsDynamicSqlConcatenation(ScalarExpression? expression)
    {
        if (expression is FunctionCall functionCall)
        {
            string functionName = functionCall.FunctionName.Value;
            if (!string.Equals(functionName, "CONCAT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(functionName, "CONCAT_WS", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            StringVariableReferenceFinder functionVariables = new();
            expression.Accept(functionVariables);

            return functionVariables.VariableNames.Count > 0;
        }

        if (expression is not BinaryExpression binary
            || binary.BinaryExpressionType is not (BinaryExpressionType.Add or BinaryExpressionType.Concat))
        {
            return false;
        }

        StringLiteralFinder finder = new();
        expression.Accept(finder);

        if (finder.Found)
        {
            return true;
        }

        StringVariableReferenceFinder stringVariableFinder = new();
        expression.Accept(stringVariableFinder);

        return stringVariableFinder.VariableNames.Any(_stringVariables.Contains);
    }

    private void CaptureExecutionState(TSqlFragment node)
    {
        ConstantVariableExecutionState current = new(
            _valueStore.Values,
            _valueStore.ResolvedTemporalVariables,
            _valueStore.DynamicSqlVariables.ToHashSet(StringComparer.OrdinalIgnoreCase));

        if (_executionStates.TryGetValue(node, out var previous))
        {
            _executionStates[node] = ConstantVariableExecutionState.Merge(previous, current);

            return;
        }

        _executionStates[node] = current;
    }

    private static bool IsStringType(DataTypeReference dataType)
    {
        if (dataType is not SqlDataTypeReference sqlDataType)
        {
            return false;
        }

        return sqlDataType.SqlDataTypeOption.ToString() is "Char" or "NChar" or "VarChar" or "NVarChar" or "Text" or "NText";
    }

    private sealed class StringLiteralFinder : TSqlFragmentVisitor
    {
        internal bool Found { get; private set; }

        public override void Visit(StringLiteral node)
        {
            Found = true;
        }
    }

    private sealed class DynamicSqlVariableReferenceFinder : TSqlFragmentVisitor
    {
        internal HashSet<string> VariableNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(VariableReference node)
        {
            VariableNames.Add(NormalizeVariableName(node.Name));
        }
    }

    private sealed class StringVariableReferenceFinder : TSqlFragmentVisitor
    {
        internal HashSet<string> VariableNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(VariableReference node)
        {
            VariableNames.Add(NormalizeVariableName(node.Name));
        }
    }

    private static string NormalizeVariableName(string variableName)
    {
        return variableName.TrimStart('@');
    }

}

internal sealed record ConstantVariableExecutionState(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables,
    IReadOnlySet<string> DynamicSqlVariables
)
{
    internal static ConstantVariableExecutionState Merge(
        ConstantVariableExecutionState first,
        ConstantVariableExecutionState second
    )
    {
        ConstantVariableEnvironmentState merged = ConstantVariableEnvironmentState.Merge(
            new ConstantVariableEnvironmentState(
                first.Values,
                first.TemporalVariables,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                first.DynamicSqlVariables),
            new ConstantVariableEnvironmentState(
                second.Values,
                second.TemporalVariables,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                second.DynamicSqlVariables));

        return new ConstantVariableExecutionState(
            merged.Values,
            merged.TemporalVariables,
            merged.DynamicSqlVariables);
    }
}

internal sealed record ConstantVariableEnvironmentResult(
    IReadOnlyDictionary<string, ScalarExpression> Values,
    IReadOnlySet<string> TemporalVariables,
    IReadOnlyDictionary<TSqlFragment, ConstantVariableExecutionState> ExecutionStates
);
