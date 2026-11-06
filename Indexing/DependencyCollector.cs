using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>
/// Walks the parse tree of one batch and records which object does what to which object.
/// "source_object" is the procedure, function, view or trigger the statement sits in; statements
/// outside any of them belong to "(batch)".
/// </summary>
public sealed partial class DependencyCollector(
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

    public override void Visit(InsertStatement node)
    {
        var target = RecordWriteTarget(node.InsertSpecification.Target, null, "INSERT");
        RecordInsertColumns(target, node.InsertSpecification.Columns);
    }

    public override void Visit(UpdateStatement node)
    {
        var specification = node.UpdateSpecification;
        ReportUpdateSafety(node, specification);

        var target = RecordWriteTarget(specification.Target, specification.FromClause, "UPDATE");
        RecordUpdateColumns(target, specification.SetClauses);
    }

    public override void Visit(DeleteStatement node)
    {
        ReportDeleteSafety(node);

        RecordWriteTarget(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause, "DELETE");
    }

    private void ReportUpdateSafety(UpdateStatement statement, UpdateSpecification specification)
    {
        if (specification.WhereClause is not null)
        {
            ReportConstantDmlFilter(specification.WhereClause.SearchCondition, statement);

            return;
        }

        if (IsUpdateSafetyExempt(statement, specification))
        {
            return;
        }

        AddLint("update-without-where", "warning", statement, "UPDATE has no WHERE clause and can modify every matching row.");
    }

    private void ReportDeleteSafety(DeleteStatement statement)
    {
        var specification = statement.DeleteSpecification;

        if (specification.WhereClause is not null)
        {
            ReportConstantDmlFilter(specification.WhereClause.SearchCondition, statement);

            return;
        }

        if (IsDeleteSafetyExempt(statement, specification))
        {
            return;
        }

        AddLint("delete-without-where", "warning", statement, "DELETE has no WHERE clause and can remove every matching row.");
    }

    private static bool IsUpdateSafetyExempt(UpdateStatement statement, UpdateSpecification specification)
    {
        if (specification.TopRowFilter is not null)
        {
            return true;
        }

        if (IsLocalDmlTarget(specification.Target, specification.FromClause))
        {
            return true;
        }

        if (IsFilteredCteUpdate(statement))
        {
            return true;
        }

        if (IsRestrictedDerivedUpdate(statement))
        {
            return true;
        }

        return HasJoinPredicate(specification.FromClause);
    }

    private static bool IsDeleteSafetyExempt(DeleteStatement statement, DeleteSpecification specification)
    {
        if (specification.TopRowFilter is not null)
        {
            return true;
        }

        if (IsLocalDmlTarget(specification.Target, specification.FromClause))
        {
            return true;
        }

        if (IsTopLimitedCteDelete(statement))
        {
            return true;
        }

        return HasJoinPredicate(specification.FromClause);
    }

    private static bool IsLocalDmlTarget(TableReference? target, FromClause? fromClause)
    {
        if (target is VariableTableReference)
        {
            return true;
        }

        if (target is not NamedTableReference named)
        {
            return false;
        }

        if (ObjectNames.IsTemporary(named.SchemaObject))
        {
            return true;
        }

        if (fromClause is null)
        {
            return false;
        }

        if (ObjectNames.IsTemporary(ResolveAlias(named, fromClause).SchemaObject))
        {
            return true;
        }

        return VariableTableReferenceFinder.Find(fromClause).Any(reference =>
            string.Equals(reference.Alias?.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTopLimitedCteDelete(DeleteStatement statement)
    {
        var cte = FindTargetCte(statement.DeleteSpecification.Target, statement.WithCtesAndXmlNamespaces);

        if (cte is null)
        {
            return false;
        }

        return HasDirectTopRowFilter(cte.QueryExpression);
    }

    private static bool IsFilteredCteUpdate(UpdateStatement statement)
    {
        var cte = FindTargetCte(statement.UpdateSpecification.Target, statement.WithCtesAndXmlNamespaces);

        if (cte is null)
        {
            return false;
        }

        return HasDirectWhereClause(cte.QueryExpression);
    }

    private static CommonTableExpression? FindTargetCte(
        TableReference? target,
        WithCtesAndXmlNamespaces? withCtes)
    {
        if (target is not NamedTableReference named)
        {
            return null;
        }

        if (named.SchemaObject.SchemaIdentifier is not null)
        {
            return null;
        }

        if (withCtes is null)
        {
            return null;
        }

        return withCtes.CommonTableExpressions.FirstOrDefault(expression =>
            string.Equals(expression.ExpressionName.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRestrictedDerivedUpdate(UpdateStatement statement)
    {
        if (statement.UpdateSpecification.Target is not NamedTableReference target)
        {
            return false;
        }

        var derived = DirectQueryDerivedTableFinder.Find(statement.UpdateSpecification.FromClause)
            .FirstOrDefault(table => string.Equals(table.Alias?.Value, target.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));

        return derived is not null && HasDirectRestriction(derived.QueryExpression);
    }

    private static bool HasDirectTopRowFilter(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.TopRowFilter is not null,
        QueryParenthesisExpression parenthesized => HasDirectTopRowFilter(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasDirectTopRowFilter(binary.FirstQueryExpression)
            || HasDirectTopRowFilter(binary.SecondQueryExpression),
        _ => false,
    };

    private static bool HasDirectWhereClause(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.WhereClause is not null,
        QueryParenthesisExpression parenthesized => HasDirectWhereClause(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasDirectWhereClause(binary.FirstQueryExpression)
            || HasDirectWhereClause(binary.SecondQueryExpression),
        _ => false,
    };

    private static bool HasDirectRestriction(QueryExpression expression)
    {
        if (HasDirectWhereClause(expression))
        {
            return true;
        }

        return HasDirectTopRowFilter(expression);
    }

    private static bool HasJoinPredicate(FromClause? fromClause) => fromClause?.TableReferences.Any(HasJoinPredicate) == true;

    private static bool HasJoinPredicate(TableReference table) => table switch
    {
        QualifiedJoin join => join.SearchCondition is not null
            || HasJoinPredicate(join.FirstTableReference)
            || HasJoinPredicate(join.SecondTableReference),
        JoinTableReference join => HasJoinPredicate(join.FirstTableReference)
            || HasJoinPredicate(join.SecondTableReference),
        _ => false,
    };

    private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
        inner.StartOffset >= outer.StartOffset
        && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;

    /// <summary>Reports only DML filters whose truth value is determined entirely by literals.</summary>
    private void ReportConstantDmlFilter(BooleanExpression condition, TSqlFragment statement)
    {
        var result = ConstantExpressionEvaluator.Evaluate(condition);
        if (result is null)
        {
            ReportUnresolvedConstantFilter(condition, statement);

            return;
        }

        var rule = result.Value ? "dml-ineffective-filter" : "dml-always-false-filter";
        var message = result.Value
            ? "DML WHERE condition is always true and does not restrict the target rows."
            : "DML WHERE condition is always false and will not affect any rows.";
        AddLint(rule, "info", statement, message);
    }

    private void ReportUnresolvedConstantFilter(BooleanExpression condition, TSqlFragment statement)
    {
        if (!HasIneffectiveConstantBranch(condition) || ContainsUnstableOperand(condition))
        {
            return;
        }

        AddLint(
            "dml-ineffective-filter",
            "info",
            statement,
            "DML WHERE condition contains a constant branch that does not restrict the target rows."
        );
    }

    private static bool HasIneffectiveConstantBranch(BooleanExpression expression)
    {
        if (expression is not BooleanBinaryExpression binary)
        {
            return false;
        }

        var first = ConstantExpressionEvaluator.Evaluate(binary.FirstExpression);
        var second = ConstantExpressionEvaluator.Evaluate(binary.SecondExpression);
        var ineffective = binary.BinaryExpressionType == BooleanBinaryExpressionType.And
            ? first == true || second == true
            : first == false || second == false;

        return ineffective
            || HasIneffectiveConstantBranch(binary.FirstExpression)
            || HasIneffectiveConstantBranch(binary.SecondExpression);
    }

    private static bool ContainsUnstableOperand(BooleanExpression expression)
    {
        UnstableOperandVisitor visitor = new();
        expression.Accept(visitor);

        return visitor.Found;
    }

    private sealed class UnstableOperandVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(VariableReference node)
        {
            Found = true;
        }

        public override void Visit(FunctionCall node)
        {
            if (node.FunctionName is not null && NondeterministicFunctions.Contains(node.FunctionName.Value))
            {
                Found = true;
            }

            base.Visit(node);
        }
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
        if (expression is FunctionCall function
            && function.Parameters.OfType<ColumnReferenceExpression>().FirstOrDefault() is { } reference)
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

        if (literal.Value.Length == 0 || literal.Value[0] != '%')
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
        if (firstCount is not null && secondCount is not null && firstCount != secondCount)
        {
            AddLint("set-operation-column-count", "error", node, $"{node.BinaryQueryExpressionType} requires both queries to return the same number of columns ({firstCount} and {secondCount}).");
        }
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

            if (ObjectNames.IsTemporary(node.Into)
                && SelectElementCount(node.QueryExpression) is { } columnCount)
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

    private string GenerateSql(TSqlFragment? fragment)
    {
        if (fragment is null)
        {
            return "unknown";
        }

        return GenerateScriptText(fragment).Trim();
    }

    private string GenerateScriptText(TSqlFragment fragment)
    {
        var started = Stopwatch.GetTimestamp();
        _scriptGenerator.GenerateScript(fragment, out var sql);
        _generateScriptTicks += Stopwatch.GetTimestamp() - started;
        _generateScriptCalls++;

        return sql;
    }

    private string GenerateSourceOrSql(TSqlFragment fragment)
    {
        if (TryReadSourceSql(fragment, out var sourceSql))
        {
            _sourceSliceHits++;

            return sourceSql;
        }

        return GenerateSql(fragment);
    }

    private bool TryReadSourceSql(TSqlFragment fragment, out string sql)
    {
        sql = "";

        if (_sourceText is null)
        {
            return false;
        }

        if (fragment.StartOffset < 0)
        {
            return false;
        }

        if (fragment.FragmentLength <= 0)
        {
            return false;
        }

        if (fragment.StartOffset + fragment.FragmentLength > _sourceText.Length)
        {
            return false;
        }

        sql = _sourceText.AsSpan(fragment.StartOffset, fragment.FragmentLength).Trim().ToString();

        return sql.Length > 0;
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

            if (element is not SelectStarExpression star
                || SelectStarColumnCount(specification.FromClause, star.Qualifier) is not { } starCount)
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
            if (tables.Count == 0 || tables.Any(table => !_temporaryTableColumnCounts.ContainsKey(table.SchemaObject.BaseIdentifier.Value)))
            {
                return null;
            }

            return tables.Sum(table => _temporaryTableColumnCounts[table.SchemaObject.BaseIdentifier.Value]);
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

    private static bool MatchesTableQualifier(NamedTableReference table, MultiPartIdentifier qualifier)
    {
        var name = qualifier.Identifiers.LastOrDefault()?.Value;
        return name is not null
            && (string.Equals(table.Alias?.Value, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase));
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
