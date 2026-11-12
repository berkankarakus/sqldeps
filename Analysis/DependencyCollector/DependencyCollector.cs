using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>
/// Walks the parse tree of one batch and records which object does what to which object.
/// "source_object" is the procedure, function, view or trigger the statement sits in; statements
/// outside any of them belong to "(batch)".
/// </summary>
internal sealed partial class DependencyCollector(
    string sourceFile,
    int batchStartLine,
    HashSet<string> cteNames,
    string defaultSchema,
    string? sourceText = null
) : TSqlFragmentVisitor
{
    private const string LooseBatch = "(batch)";
    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AVG", "COUNT", "COUNT_BIG", "MAX", "MIN", "SUM", "STRING_AGG"
    };

    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART", "DATETRUNC"
    };

    private static readonly HashSet<string> NondeterministicFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GETDATE",
        "GETUTCDATE",
        "NEWID",
        "NEWSEQUENTIALID",
        "RAND",
        "SYSDATETIME",
        "SYSDATETIMEOFFSET",
        "SYSUTCDATETIME"
    };

    // Methods on XML columns look like function calls on an object (col.value(...)), not like UDFs.
    private static readonly HashSet<string> XmlMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "value",
        "query",
        "exist",
        "nodes",
        "modify"
    };

    private readonly List<DatabaseObject> _objects = [];
    private readonly List<Dependency> _dependencies = [];
    private readonly List<ObjectParameter> _parameters = [];
    private readonly List<ResultColumn> _resultColumns = [];
    private readonly List<ColumnUsage> _columnUsage = [];
    private readonly List<TableColumn> _tableColumns = [];
    private readonly List<TableKey> _tableKeys = [];
    private readonly List<TableForeignKey> _foreignKeys = [];
    private readonly List<TableIndexColumn> _indexColumns = [];
    private readonly List<ParseProblem> _problems = [];
    private readonly List<LintFinding> _lintFindings = [];
    private readonly HashSet<LintFinding> _lintFindingSet = [];
    private readonly TSql160Parser _parser = new(initialQuotedIdentifiers: true);
    private readonly Sql160ScriptGenerator _scriptGenerator = new();
    private readonly string? _sourceText = sourceText;
    private long _generateScriptTicks;
    private int _generateScriptCalls;
    private int _sourceSliceHits;
    private string _scope = LooseBatch;
    private bool _transactionActive;
    private IReadOnlyDictionary<string, ScalarExpression> _constantVariables =
        new Dictionary<string, ScalarExpression>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _temporalVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Table references already recorded as the target of a write, so the walk does not also count them as reads.
    private readonly HashSet<TSqlFragment> _handled = [];
    private readonly HashSet<QuerySpecification> _insertSelectQueries = [];
    private readonly HashSet<BooleanExpression> _dmlFalseInnerJoinConditions = [];
    private readonly Dictionary<string, int> _temporaryTableColumnCounts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TryCatchStatement> _tryCatchStatements = [];
    private IReadOnlyDictionary<QuerySpecification, IReadOnlySet<string>> _correlatedAliases =
        new Dictionary<QuerySpecification, IReadOnlySet<string>>();

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => _parameters;

    public IReadOnlyList<ResultColumn> ResultColumns => _resultColumns;

    public IReadOnlyList<ColumnUsage> ColumnUsage => _columnUsage;
    public IReadOnlyList<TableColumn> TableColumns => _tableColumns;

    public IReadOnlyList<TableKey> TableKeys => _tableKeys;

    public IReadOnlyList<TableForeignKey> ForeignKeys => _foreignKeys;

    public IReadOnlyList<TableIndexColumn> IndexColumns => _indexColumns;

    public IReadOnlyList<ParseProblem> Problems => _problems;
    public IReadOnlyList<LintFinding> LintFindings => _lintFindings;

    public long GenerateScriptTicks => _generateScriptTicks;

    public int GenerateScriptCalls => _generateScriptCalls;

    public int SourceSliceHits => _sourceSliceHits;

    internal DependencyCollectionResult CreateContribution() => new(
        _objects,
        _dependencies,
        _columnUsage,
        _parameters,
        _resultColumns,
        _tableKeys,
        _indexColumns,
        _foreignKeys,
        _tableColumns,
        _problems,
        _lintFindings
    );

    // ---- objects a script defines -------------------------------------------------------------

    /// <summary>
    /// Walks a script statement by statement. A CREATE or ALTER of a procedure, function, view or
    /// trigger sets the scope its body is recorded under; any other statement belongs to "(batch)".
    /// </summary>
    public void Collect(TSqlScript script)
    {
        _tryCatchStatements = TryCatchStatementFinder.Find(script);

        foreach (var batch in script.Batches)
        {
            _transactionActive = false;
            var variableEnvironment = ConstantVariableEnvironment.Find(batch);
            _constantVariables = variableEnvironment.Values;
            _temporalVariables = variableEnvironment.TemporalVariables;

            foreach (var statement in batch.Statements)
            {
                _scope = DefineObject(statement);
                _correlatedAliases = BuildCorrelatedAliases(statement);

                try
                {
                    statement.Accept(this);
                }
                finally
                {
                    _correlatedAliases = new Dictionary<QuerySpecification, IReadOnlySet<string>>();
                    _insertSelectQueries.Clear();
                    _dmlFalseInnerJoinConditions.Clear();
                    _scope = LooseBatch;
                }
            }

            _constantVariables = new Dictionary<string, ScalarExpression>(StringComparer.OrdinalIgnoreCase);
            _temporalVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool? EvaluateCondition(BooleanExpression expression)
    {
        return _constantVariables.Count == 0
            ? ConstantExpressionEvaluator.Evaluate(expression)
            : ConstantExpressionEvaluator.Evaluate(expression, _constantVariables, _temporalVariables);
    }

    private void AddParameters(
        string objectName,
        IList<ProcedureParameter> parameters
    )
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];

            _parameters.Add(new ObjectParameter(
                objectName,
                index + 1,
                parameter.VariableName.Value,
                GenerateSql(parameter.DataType),
                parameter.Value is null ? null : GenerateSql(parameter.Value),
                parameter.Modifier == ParameterModifier.Output,
                sourceFile
            ));
        }
    }

    private static bool TryIntegerLiteral(ScalarExpression? expression, out long value)
    {
        value = 0;
        return expression switch
        {
            IntegerLiteral literal => long.TryParse(literal.Value, out value),
            ParenthesisExpression { Expression: var inner } => TryIntegerLiteral(inner, out value),
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Positive, Expression: var inner }
                => TryIntegerLiteral(inner, out value),
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: var inner }
                => TryNegativeIntegerLiteral(inner, out value),
            _ => false,
        };
    }

    private static bool TryNegativeIntegerLiteral(ScalarExpression expression, out long value)
    {
        value = 0;
        if (!TryIntegerLiteral(expression, out var absolute) || absolute == long.MinValue)
        {
            return false;
        }

        value = -absolute;
        return true;
    }

    private string Format(SchemaObjectName name)
    {
        return ObjectNames.Format(name, defaultSchema);
    }

    private int LineOf(TSqlFragment fragment)
    {
        return batchStartLine + fragment.StartLine - 1;
    }

    private void AddLint(string rule, string severity, TSqlFragment node, string message)
    {
        var diagnostic = LintDiagnosticFactory.Create(rule, severity, message);
        var finding = LintDiagnosticFactory.CreateFinding(diagnostic, _scope, sourceFile, LineOf(node), node);
        if (_lintFindingSet.Add(finding))
        {
            _lintFindings.Add(finding);
        }
    }

}
