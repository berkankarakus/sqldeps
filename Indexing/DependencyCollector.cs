using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

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
    private readonly TSql160Parser _parser = new(initialQuotedIdentifiers: true);
    private readonly Sql160ScriptGenerator _scriptGenerator = new();
    private readonly string? _sourceText = sourceText;
    private long _generateScriptTicks;
    private int _generateScriptCalls;
    private int _sourceSliceHits;
    private string _scope = LooseBatch;
    private bool _transactionActive;

    // Table references already recorded as the target of a write, so the walk does not also count them as reads.
    private readonly HashSet<TSqlFragment> _handled = [];
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
                    _scope = LooseBatch;
                }
            }
        }
    }

    // ---- reads and writes -----------------------------------------------------------------------

    public override void Visit(NamedTableReference node)
    {
        if (node.TableHints.Any(hint => hint.HintKind == TableHintKind.NoLock))
        {
            AddLint("nolock", "warning", node, "NOLOCK can return uncommitted or inconsistent data.");

            if (_transactionActive)
            {
                AddLint("nolock-in-transaction", "warning", node, "NOLOCK inside a transaction can combine dirty reads with transaction-scoped changes; verify that this is intentional.");
            }
        }

        if (node.TableHints.Any(hint => hint.HintKind == TableHintKind.ReadUncommitted))
        {
            AddLint("read-uncommitted", "warning", node, "READUNCOMMITTED can return uncommitted or inconsistent data.");
        }

        if (_handled.Contains(node))
        {
            return;
        }

        AddTableDependency(node.SchemaObject, "SELECT", node);
    }

    public override void Visit(TruncateTableStatement node)
    {
        AddTableDependency(node.TableName, "TRUNCATE", node);
    }

    public override void Visit(BeginTransactionStatement node)
    {
        _transactionActive = true;

        if (!_tryCatchStatements.Any(statement => Contains(statement, node) || IsImmediatelyProtectedBy(statement, node)))
        {
            AddLint("transaction-without-error-handling", "warning", node, "BEGIN TRANSACTION requires explicit TRY/CATCH and rollback handling on every failure path.");
        }
    }

    private bool IsImmediatelyProtectedBy(TryCatchStatement tryCatch, BeginTransactionStatement transaction)
    {
        if (_sourceText is null)
        {
            return false;
        }

        if (tryCatch.StartOffset < transaction.StartOffset + transaction.FragmentLength)
        {
            return false;
        }

        if (tryCatch.CatchStatements is null)
        {
            return false;
        }

        if (!OnlySqlTrivia(transaction.StartOffset + transaction.FragmentLength, tryCatch.StartOffset))
        {
            return false;
        }

        RollbackTransactionFinder rollback = new();
        tryCatch.CatchStatements.Accept(rollback);

        return rollback.Found;
    }

    private bool OnlySqlTrivia(int startOffset, int endOffset)
    {
        if (_sourceText is null)
        {
            return false;
        }

        if (startOffset < 0)
        {
            return false;
        }

        if (endOffset < startOffset)
        {
            return false;
        }

        if (endOffset > _sourceText.Length)
        {
            return false;
        }

        var text = _sourceText.AsSpan(startOffset, endOffset - startOffset);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                continue;
            }

            if (IsLineCommentStart(text, index))
            {
                SkipLineComment(text, ref index);
                continue;
            }

            if (IsBlockCommentStart(text, index))
            {
                if (!TrySkipBlockComment(text, ref index))
                {
                    return false;
                }

                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsLineCommentStart(ReadOnlySpan<char> text, int index)
    {
        return text[index] == '-'
            && index + 1 < text.Length
            && text[index + 1] == '-';
    }

    private static void SkipLineComment(ReadOnlySpan<char> text, ref int index)
    {
        index += 2;

        while (index < text.Length && text[index] is not '\r' and not '\n')
        {
            index++;
        }

        index--;
    }

    private static bool IsBlockCommentStart(ReadOnlySpan<char> text, int index)
    {
        return text[index] == '/'
            && index + 1 < text.Length
            && text[index + 1] == '*';
    }

    private static bool TrySkipBlockComment(ReadOnlySpan<char> text, ref int index)
    {
        var close = text[(index + 2)..].IndexOf("*/", StringComparison.Ordinal);

        if (close < 0)
        {
            return false;
        }

        index += close + 3;

        return true;
    }

    public override void Visit(CommitTransactionStatement node)
    {
        _transactionActive = false;
    }

    public override void Visit(RollbackTransactionStatement node)
    {
        _transactionActive = false;
    }

    public override void Visit(SelectStatement node)
    {
        // SELECT ... INTO dbo.Copy creates and fills a table.
        if (node.Into is not null)
        {
            AddTableDependency(node.Into, "INSERT", node);

            if (!ObjectNames.IsTemporary(node.Into))
            {
                return;
            }

            if (SelectElementCount(node.QueryExpression) is { } columnCount)
            {
                _temporaryTableColumnCounts[node.Into.BaseIdentifier.Value] = columnCount;
            }
        }
    }

    public override void Visit(TopRowFilter node)
    {
        ReportTopRowFilter(node);
    }

    private void ReportTopRowFilter(TopRowFilter node)
    {
        if (TryIntegerLiteral(node.Expression, out var count) && count == 0)
        {
            AddLint("top-zero", "info", node, "TOP (0) always returns no rows; use it only when an empty result is intentional.");
        }
    }

    public override void Visit(OffsetClause node)
    {
        if (TryIntegerLiteral(node.OffsetExpression, out var offset))
        {
            if (offset < 0)
            {
                AddLint("negative-offset", "error", node.OffsetExpression, "OFFSET cannot be negative.");
            }
        }

        if (node.FetchExpression is null)
        {
            return;
        }

        if (!TryIntegerLiteral(node.FetchExpression, out var fetch))
        {
            return;
        }

        if (fetch <= 0)
        {
            AddLint("invalid-fetch", "error", node.FetchExpression, "FETCH row count must be greater than zero.");
        }
    }

    private void AddTableDependency(
        SchemaObjectName name,
        string operation,
        TSqlFragment at
    )
    {
        if (IsTransientTable(name))
        {
            return;
        }

        if (IsCommonTableExpression(name))
        {
            return;
        }

        AddDependency(Format(name), operation, at);
    }

    private void AddDependency(
        string target,
        string operation,
        TSqlFragment at
    )
    {
        _dependencies.Add(new Dependency(_scope, target, operation, sourceFile, LineOf(at)));
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
        _lintFindings.Add(LintDiagnosticFactory.CreateFinding(diagnostic, _scope, sourceFile, LineOf(node), node));
    }

}
