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

    public override void Visit(SelectStarExpression node)
    {
        AddLint("select-star", "info", node, "SELECT * makes result shape and schema changes harder to reason about.");
    }

    public override void Visit(BooleanComparisonExpression node)
    {
        ReportNullComparison(node);

        ReportNonSargableComparison(node);
    }

    private void ReportNonSargableComparison(BooleanComparisonExpression node)
    {
        if (!TryGetComparisonColumn(node, out var column))
        {
            return;
        }

        AddLint("non-sargable-function", "info", column, "A function is applied to a column in a comparison; this can prevent an index seek. Compare the raw column when possible.");
    }

    private static bool TryGetComparisonColumn(
        BooleanComparisonExpression comparison,
        out ColumnReferenceExpression column)
    {
        if (TryGetWrappedColumn(comparison.FirstExpression, out column))
        {
            return true;
        }

        return TryGetWrappedColumn(comparison.SecondExpression, out column);
    }

    private void ReportNullComparison(BooleanComparisonExpression node)
    {
        if (!HasNullOperand(node))
        {
            return;
        }

        var replacement = node.ComparisonType == BooleanComparisonType.Equals ? "IS NULL" : "IS NOT NULL";

        if (node.ComparisonType is BooleanComparisonType.Equals
            or BooleanComparisonType.NotEqualToBrackets
            or BooleanComparisonType.NotEqualToExclamation)
        {
            AddLint("null-comparison", "warning", node, $"Comparison with NULL never evaluates as expected; use {replacement} instead.");
        }
    }

    private static bool TryGetWrappedColumn(ScalarExpression expression, out ColumnReferenceExpression column)
    {
        if (expression is not FunctionCall function)
        {
            column = null!;

            return false;
        }

        if (function.Parameters.OfType<ColumnReferenceExpression>().FirstOrDefault() is { } reference)
        {
            column = reference;

            return true;
        }

        column = null!;

        return false;
    }

    public override void Visit(LikePredicate node)
    {
        if (node.SecondExpression is not StringLiteral literal)
        {
            return;
        }

        if (literal.Value.Length == 0)
        {
            return;
        }

        if (literal.Value[0] != '%')
        {
            return;
        }

        AddLint("leading-wildcard-like", "info", node, "LIKE pattern starts with '%', which usually prevents an index seek.");
    }

    public override void Visit(InPredicate node)
    {
        if (!node.NotDefined)
        {
            return;
        }

        if (!node.Values.Any(value => value is NullLiteral))
        {
            return;
        }

        AddLint("not-in-null", "warning", node, "NOT IN with NULL never matches; remove NULL or use NOT EXISTS.");
    }

    public override void Visit(SearchedCaseExpression node)
    {
        ReportMixedCaseConstants(node, node.WhenClauses);
    }

    public override void Visit(SimpleCaseExpression node)
    {
        ReportMixedCaseConstants(node, node.WhenClauses);
    }

    private void ReportMixedCaseConstants(CaseExpression node, IEnumerable<WhenClause> clauses)
    {
        var types = clauses
            .Select(clause => ConstantType(clause.ThenExpression))
            .Append(ConstantType(node.ElseExpression))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (types.Length > 1)
        {
            AddLint("case-mixed-constants", "info", node, $"CASE returns mixed constant types ({string.Join(" and ", types)}); SQL Server will apply an implicit conversion.");
        }
    }

    private static string? ConstantType(ScalarExpression? expression) => expression switch
    {
        StringLiteral => "string",
        IntegerLiteral or NumericLiteral or MoneyLiteral => "number",
        NullLiteral => null,
        _ => null,
    };

    public override void Visit(BinaryQueryExpression node)
    {
        var firstCount = SelectElementCount(node.FirstQueryExpression);
        var secondCount = SelectElementCount(node.SecondQueryExpression);

        if (firstCount is null || secondCount is null)
        {
            return;
        }

        if (firstCount == secondCount)
        {
            return;
        }

        AddLint("set-operation-column-count", "error", node, $"{node.BinaryQueryExpressionType} requires both queries to return the same number of columns ({firstCount} and {secondCount}).");
    }

    public override void Visit(MergeStatement node)
    {
        AddLint("merge", "warning", node, "MERGE has concurrency and correctness edge cases; verify its behavior under concurrent writes.");
        var specification = node.MergeSpecification;
        RecordWriteTarget(specification.Target, null, "MERGE");

        if (HasUnorderedTopInMergeSource(specification.TableReference))
        {
            AddLint("merge-top-without-order", "warning", specification.TableReference, "MERGE source uses TOP without ORDER BY; source row selection is nondeterministic.");
        }

        if (ShouldReportNondeterministicMerge(specification.SearchCondition))
        {
            AddLint("merge-nondeterministic-match", "warning", (TSqlFragment?)specification.SearchCondition ?? node, "MERGE match condition has no equality between two column references; verify that one source row cannot match multiple target rows.");
        }

        foreach (var clause in specification.ActionClauses)
        {
            var operation = MergeOperationOf(clause.Action);

            if (operation is not null)
            {
                RecordWriteTarget(specification.Target, null, operation);
            }

            if (clause.Action is UpdateMergeAction update)
            {
                ReportDuplicateMergeAssignments(update);
            }
        }
    }

    private static bool ShouldReportNondeterministicMerge(BooleanExpression searchCondition)
    {
        var constantResult = ConstantExpressionEvaluator.Evaluate(searchCondition);

        if (constantResult is false)
        {
            return false;
        }

        return !HasDeterministicMergeMatch(searchCondition);
    }

    private static bool HasUnorderedTopInMergeSource(TableReference source) => source switch
    {
        QueryDerivedTable derived => HasUnorderedTop(derived.QueryExpression),
        _ => false,
    };

    private static bool HasUnorderedTop(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.TopRowFilter is not null && specification.OrderByClause is null,
        QueryParenthesisExpression parenthesized => HasUnorderedTop(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasUnorderedTop(binary.FirstQueryExpression)
            || HasUnorderedTop(binary.SecondQueryExpression),
        _ => false,
    };

    private void ReportDuplicateMergeAssignments(UpdateMergeAction update)
    {
        foreach (var assignments in update.SetClauses
            .OfType<AssignmentSetClause>()
            .Where(assignment => assignment.Column?.MultiPartIdentifier?.Identifiers.Count > 0)
            .GroupBy(assignment => assignment.Column!.MultiPartIdentifier!.Identifiers[^1].Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var assignment in assignments.Skip(1))
            {
                AddLint("duplicate-merge-column", "error", assignment.Column!, $"Column '{assignments.Key}' is assigned more than once in this MERGE action.");
            }
        }
    }

    private static bool HasDeterministicMergeMatch(BooleanExpression? condition)
    {
        if (condition is null)
        {
            return false;
        }

        MergeMatchVisitor visitor = new();
        condition.Accept(visitor);

        return visitor.HasColumnEquality;
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

    private static bool HasNullOperand(BooleanComparisonExpression node)
    {
        return node.FirstExpression is NullLiteral || node.SecondExpression is NullLiteral;
    }

    public override void Visit(CommonTableExpression node)
    {
        var selectCount = SelectElementCount(node.QueryExpression);

        if (node.Columns.Count == 0 || selectCount is null)
        {
            return;
        }

        if (node.Columns.Count == selectCount)
        {
            return;
        }

        AddLint(
            "cte-column-count",
            "error",
            node,
            $"CTE '{node.ExpressionName.Value}' declares {node.Columns.Count} column(s) but its query returns {selectCount}."
        );
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

    private static string? MergeOperationOf(MergeAction action)
    {
        if (action is UpdateMergeAction)
        {
            return "UPDATE";
        }

        if (action is InsertMergeAction)
        {
            return "INSERT";
        }

        if (action is DeleteMergeAction)
        {
            return "DELETE";
        }

        return null;
    }

    private int? SelectElementCount(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => SelectElementCount(specification),
        BinaryQueryExpression binary when SelectElementCount(binary.FirstQueryExpression) is { } first
            && SelectElementCount(binary.SecondQueryExpression) == first => first,
        _ => null,
    };

    private int? SelectElementCount(QuerySpecification specification)
    {
        var count = 0;
        foreach (var element in specification.SelectElements)
        {
            if (element is SelectScalarExpression)
            {
                count++;
                continue;
            }

            if (element is not SelectStarExpression star)
            {
                return null;
            }

            if (SelectStarColumnCount(specification.FromClause, star.Qualifier) is not { } starCount)
            {
                return null;
            }

            count += starCount;
        }

        return count;
    }

    private int? SelectStarColumnCount(FromClause? fromClause, MultiPartIdentifier? qualifier)
    {
        var tables = DirectTableAliasFinder.FindScope(fromClause).Tables;

        if (qualifier is null)
        {
            return SelectUnqualifiedStarColumnCount(tables);
        }

        var matchingTables = tables.Where(table => MatchesTableQualifier(table, qualifier)).ToArray();

        if (matchingTables.Length != 1)
        {
            return null;
        }

        return _temporaryTableColumnCounts.TryGetValue(matchingTables[0].SchemaObject.BaseIdentifier.Value, out var count)
            ? count
            : null;
    }

    private int? SelectUnqualifiedStarColumnCount(IReadOnlyList<NamedTableReference> tables)
    {
        if (!HasKnownTemporaryTableColumns(tables))
        {
            return null;
        }

        return tables.Sum(table => _temporaryTableColumnCounts[table.SchemaObject.BaseIdentifier.Value]);
    }

    private bool HasKnownTemporaryTableColumns(IReadOnlyList<NamedTableReference> tables)
    {
        if (tables.Count == 0)
        {
            return false;
        }

        return tables.All(table => _temporaryTableColumnCounts.ContainsKey(table.SchemaObject.BaseIdentifier.Value));
    }

    private static bool MatchesTableQualifier(NamedTableReference table, MultiPartIdentifier qualifier)
    {
        var name = qualifier.Identifiers.LastOrDefault()?.Value;

        if (name is null)
        {
            return false;
        }

        if (string.Equals(table.Alias?.Value, name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MergeMatchVisitor : TSqlFragmentVisitor
    {
        public bool HasColumnEquality { get; private set; }

        public override void Visit(BooleanComparisonExpression node)
        {
            if (IsColumnEquality(node))
            {
                HasColumnEquality = true;
            }

            base.Visit(node);
        }

        private static bool IsColumnEquality(BooleanComparisonExpression node)
        {
            if (node.ComparisonType != BooleanComparisonType.Equals)
            {
                return false;
            }

            if (node.FirstExpression is not ColumnReferenceExpression left
                || node.SecondExpression is not ColumnReferenceExpression right)
            {
                return false;
            }

            return !string.Equals(Qualifier(left), Qualifier(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string? Qualifier(ColumnReferenceExpression column) => column.MultiPartIdentifier?.Identifiers.Count > 1
            ? column.MultiPartIdentifier.Identifiers[^2].Value
            : null;
    }

}
