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
    private readonly HashSet<string> _batchAliases = new(StringComparer.OrdinalIgnoreCase);
    private string _scope = LooseBatch;
    private bool _transactionActive;
    private bool _insideTryCatch;

    // Table references already recorded as the target of a write, so the walk does not also count them as reads.
    private readonly HashSet<TSqlFragment> _handled = [];

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

    // ---- objects a script defines -------------------------------------------------------------

    /// <summary>
    /// Walks a script statement by statement. A CREATE or ALTER of a procedure, function, view or
    /// trigger sets the scope its body is recorded under; any other statement belongs to "(batch)".
    /// </summary>
    public void Collect(TSqlScript script)
    {
        foreach (var batch in script.Batches)
        {
            _transactionActive = false;

            foreach (var statement in batch.Statements)
            {
                _scope = DefineObject(statement);
                statement.Accept(this);
                _scope = LooseBatch;
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
        if (specification.WhereClause is null)
        {
            AddLint("update-without-where", "warning", node, "UPDATE has no WHERE clause and can modify every matching row.");
        }
        else
        {
            ReportConstantDmlFilter(specification.WhereClause.SearchCondition, node);
        }
        var target = RecordWriteTarget(specification.Target, specification.FromClause, "UPDATE");
        RecordUpdateColumns(target, specification.SetClauses);
    }

    public override void Visit(DeleteStatement node)
    {
        if (node.DeleteSpecification.WhereClause is null)
        {
            AddLint("delete-without-where", "warning", node, "DELETE has no WHERE clause and can remove every matching row.");
        }
        else
        {
            ReportConstantDmlFilter(node.DeleteSpecification.WhereClause.SearchCondition, node);
        }
        RecordWriteTarget(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause, "DELETE");
    }

    /// <summary>Reports only DML filters whose truth value is determined entirely by literals.</summary>
    private void ReportConstantDmlFilter(BooleanExpression condition, TSqlFragment statement)
    {
        var result = EvaluateConstantCondition(condition);
        if (result is null)
        {
            return;
        }

        var rule = result.Value ? "dml-ineffective-filter" : "dml-always-false-filter";
        var message = result.Value
            ? "DML WHERE condition is always true and does not restrict the target rows."
            : "DML WHERE condition is always false and will not affect any rows.";
        AddLint(rule, "info", statement, message);
    }

    private static bool? EvaluateConstantCondition(BooleanExpression expression)
    {
        if (expression is BooleanParenthesisExpression parenthesis)
        {
            return EvaluateConstantCondition(parenthesis.Expression);
        }

        if (expression is BooleanBinaryExpression binary)
        {
            var first = EvaluateConstantCondition(binary.FirstExpression);
            var second = EvaluateConstantCondition(binary.SecondExpression);
            if (first is null || second is null)
            {
                return null;
            }

            return binary.BinaryExpressionType == BooleanBinaryExpressionType.And
                ? first.Value && second.Value
                : first.Value || second.Value;
        }

        if (expression is not BooleanComparisonExpression comparison
            || !TryLiteralValue(comparison.FirstExpression, out var firstValue)
            || !TryLiteralValue(comparison.SecondExpression, out var secondValue))
        {
            return null;
        }

        var comparisonResult = string.Equals(firstValue, secondValue, StringComparison.OrdinalIgnoreCase);
        return comparison.ComparisonType switch
        {
            BooleanComparisonType.Equals => comparisonResult,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => !comparisonResult,
            _ => null,
        };
    }

    private static bool TryLiteralValue(ScalarExpression expression, out string value)
    {
        value = expression switch
        {
            NumericLiteral numeric => numeric.Value,
            IntegerLiteral integer => integer.Value,
            MoneyLiteral money => money.Value,
            StringLiteral text => text.Value,
            _ => "",
        };

        return value.Length > 0;
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
        if (!TryGetWrappedColumn(node.FirstExpression, out var column)
            && !TryGetWrappedColumn(node.SecondExpression, out column))
        {
            return;
        }

        AddLint("non-sargable-function", "info", column, "A function is applied to a column in a comparison; this can prevent an index seek. Compare the raw column when possible.");
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

        if (!HasDeterministicMergeMatch(specification.SearchCondition))
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

        if (!_insideTryCatch)
        {
            AddLint("transaction-without-error-handling", "warning", node, "BEGIN TRANSACTION requires explicit TRY/CATCH and rollback handling on every failure path.");
        }
    }

    public override void Visit(TryCatchStatement node)
    {
        var wasInsideTryCatch = _insideTryCatch;
        _insideTryCatch = true;

        try
        {
            base.Visit(node);
        }
        finally
        {
            _insideTryCatch = wasInsideTryCatch;
        }
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

    public override void Visit(QuerySpecification node)
    {
        if (node.TopRowFilter is { } top)
        {
            ReportTopRowFilter(top);
        }

        var tables = TableReferenceFinder.Find(node.FromClause);
        ReportUnfilteredQuery(node, tables);

        foreach (var alias in tables.Select(table => table.Alias?.Value).OfType<string>())
        {
            _batchAliases.Add(alias);
        }

        foreach (var alias in QueryDerivedTableFinder.Find(node.FromClause)
            .Select(table => table.Alias?.Value)
            .OfType<string>())
        {
            _batchAliases.Add(alias);
        }

        var variableAliases = VariableTableReferenceFinder.Find(node.FromClause)
            .Select(reference => reference.Alias?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownAliases = new HashSet<string>(_batchAliases, StringComparer.OrdinalIgnoreCase);
        knownAliases.UnionWith(variableAliases);
        foreach (var alias in QueryDerivedTableFinder.Find(node.FromClause)
            .Select(table => table.Alias?.Value)
            .OfType<string>())
        {
            knownAliases.Add(alias);
        }

        ReportDuplicateAliases(tables);
        ReportUngroupedSelectColumns(node);

        foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
        {
            if (element.Expression is not ColumnReferenceExpression column)
            {
                continue;
            }

            ValidateAliasReference(column, tables, knownAliases, variableAliases);

            if (!TryResolveColumnTable(column, tables, out var table))
            {
                continue;
            }

            if (!TryGetColumnName(column, out var name))
            {
                continue;
            }

            AddColumnUsage(table, name, "SELECT", column);
        }
    }

    private void ReportUnfilteredQuery(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        if (query.WhereClause is not null || query.TopRowFilter is not null || tables.Count == 0)
        {
            return;
        }

        AddLint(
            "unfiltered-select",
            "info",
            query,
            "SELECT has no WHERE or TOP filter and may read many rows; verify that a broad result set is intentional."
        );
    }

    private void ReportUngroupedSelectColumns(QuerySpecification query)
    {
        if (query.GroupByClause is null)
        {
            return;
        }

        var groupedColumns = query.GroupByClause.GroupingSpecifications
            .OfType<ExpressionGroupingSpecification>()
            .SelectMany(specification => UngroupedColumns(specification.Expression))
            .Select(ColumnReferenceText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in query.SelectElements
            .OfType<SelectScalarExpression>()
            .SelectMany(element => UngroupedColumns(element.Expression)))
        {
            var name = ColumnReferenceText(column);
            if (!groupedColumns.Contains(name))
            {
                AddLint("ungrouped-select-column", "error", column, $"Column '{name}' appears in SELECT but is not included in GROUP BY or an aggregate.");
            }
        }
    }

    /// <summary>Returns columns that participate in an expression outside aggregate function arguments.</summary>
    private static IEnumerable<ColumnReferenceExpression> UngroupedColumns(ScalarExpression? expression)
    {
        if (expression is null)
        {
            return [];
        }

        ColumnReferenceVisitor visitor = new();
        expression.Accept(visitor);

        return visitor.Columns;
    }

    private sealed class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Columns { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value == "*")
            {
                return;
            }

            Columns.Add(node);
        }

        public override void Visit(FunctionCall node)
        {
            if (node.FunctionName is not null
                && AggregateFunctions.Contains(node.FunctionName.Value))
            {
                return;
            }

            base.Visit(node);
        }
    }

    private void ReportDuplicateAliases(IReadOnlyList<NamedTableReference> tables)
    {
        foreach (var duplicates in tables
            .Where(table => table.Alias is not null)
            .GroupBy(table => table.Alias!.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var duplicate in duplicates.Skip(1))
            {
                AddLint("duplicate-alias", "error", duplicate.Alias!, $"Duplicate table alias '{duplicates.Key}' in this query scope.");
            }
        }
    }

    private void ValidateAliasReference(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        IReadOnlySet<string> knownAliases,
        IReadOnlySet<string> variableAliases
    )
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count < 2)
        {
            return;
        }

        var alias = identifiers[0].Value;
        if (variableAliases.Contains(alias))
        {
            return;
        }

        var aliasIsKnown = knownAliases.Contains(alias) || IsKnownAlias(tables, alias);
        if (!aliasIsKnown && TableUsingQualifier(tables, identifiers) is { } aliasedTable)
        {
            AddLint("table-name-after-alias", "error", column, $"The multi-part identifier '{ReferenceText(column)}' could not be bound: table '{QualifierText(identifiers)}' is known as alias '{aliasedTable.Alias!.Value}' in this query.");
            return;
        }

        if (identifiers.Count == 2)
        {
            ReportTwoPartAliasReference(column, tables, alias, aliasIsKnown);
            return;
        }

        if (aliasIsKnown)
        {
            ReportInvalidMultiPartAliasReference(column, tables, alias);
            return;
        }
    }

    private void ReportTwoPartAliasReference(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        bool qualifierIsAlias
    )
    {
        if (qualifierIsAlias || HasUnaliasedTable(tables, qualifier))
        {
            return;
        }

        AddLint("unbound-identifier", "error", column, $"The multi-part identifier '{ReferenceText(column)}' could not be bound: '{qualifier}' is not a known table alias in this query.");
    }

    private void ReportInvalidMultiPartAliasReference(ColumnReferenceExpression column, IReadOnlyList<NamedTableReference> tables, string alias)
    {
        var identifiers = column.MultiPartIdentifier!.Identifiers;
        var nextPartIsAlias = tables.Any(table => string.Equals(table.Alias?.Value, identifiers[1].Value, StringComparison.OrdinalIgnoreCase));
        var message = nextPartIsAlias
            ? $"Invalid alias reference '{ReferenceText(column)}': '{alias}' is a table alias. A comma may be missing before '{identifiers[1].Value}.{identifiers[^1].Value}'."
            : $"Invalid alias reference '{ReferenceText(column)}': table alias '{alias}' can qualify only one column.";
        AddLint("invalid-alias-reference", "error", column, message);
    }

    private static bool IsKnownAlias(IEnumerable<NamedTableReference> tables, string alias) => tables.Any(table => string.Equals(table.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase));

    private NamedTableReference? TableUsingQualifier(IEnumerable<NamedTableReference> tables, IList<Identifier> identifiers)
    {
        var qualifier = QualifierText(identifiers);
        return tables.FirstOrDefault(table => table.Alias is not null
            && (string.Equals(ObjectNames.Format(table.SchemaObject, defaultSchema), qualifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasUnaliasedTable(IEnumerable<NamedTableReference> tables, string name) => tables.Any(table => table.Alias is null && string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase));

    private static string ReferenceText(ColumnReferenceExpression column) => string.Join('.', column.MultiPartIdentifier!.Identifiers.Select(identifier => identifier.Value));

    private static string QualifierText(IList<Identifier> identifiers) => string.Join('.', identifiers.Take(identifiers.Count - 1).Select(identifier => identifier.Value));

    // ---- helpers --------------------------------------------------------------------------------

    private static bool TryResolveColumnTable(
        ColumnReferenceExpression column,
        IReadOnlyList<NamedTableReference> tables,
        out NamedTableReference table
    )
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;

        if (identifiers is null || identifiers.Count == 0)
        {
            table = null!;

            return false;
        }

        if (identifiers.Count == 1 && tables.Count == 1)
        {
            table = tables[0];

            return true;
        }

        if (!TryGetQualifier(identifiers, out var qualifier))
        {
            table = null!;

            return false;
        }

        return TryFindUniqueTable(tables, qualifier, out table);
    }

    private static bool TryGetQualifier(IList<Identifier> identifiers, out string qualifier)
    {
        if (identifiers.Count < 2)
        {
            qualifier = "";

            return false;
        }

        qualifier = identifiers[identifiers.Count - 2].Value;

        return true;
    }

    private static bool TryFindUniqueTable(
        IReadOnlyList<NamedTableReference> tables,
        string qualifier,
        out NamedTableReference table
    )
    {
        table = null!;

        foreach (var reference in tables.Where(reference => MatchesQualifier(reference, qualifier)))
        {
            if (table is not null)
            {
                table = null!;

                return false;
            }

            table = reference;
        }

        return table is not null;
    }

    private static bool MatchesQualifier(NamedTableReference reference, string qualifier) =>
        string.Equals(reference.Alias?.Value, qualifier, StringComparison.OrdinalIgnoreCase)
        || string.Equals(reference.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetColumnName(
        ColumnReferenceExpression? column,
        out string name
    )
    {
        if (column is null)
        {
            name = "";

            return false;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;

        if (identifiers is null || identifiers.Count == 0)
        {
            name = "";

            return false;
        }

        name = identifiers[identifiers.Count - 1].Value;

        return true;
    }

    private static string ColumnReferenceText(ColumnReferenceExpression column) => column.MultiPartIdentifier is null
        ? ""
        : string.Join('.', column.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value));

    private void AddTableDependency(
        SchemaObjectName name,
        string operation,
        TSqlFragment at
    )
    {
        if (ObjectNames.IsTemporary(name) || ObjectNames.IsTriggerPseudoTable(name))
        {
            return;
        }

        var isCommonTableExpression = name.SchemaIdentifier is null && cteNames.Contains(name.BaseIdentifier.Value);

        if (isCommonTableExpression)
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

        _scriptGenerator.GenerateScript(fragment, out var sql);

        return sql.Trim();
    }

    private string GenerateSourceOrSql(TSqlFragment fragment)
    {
        if (TryReadSourceSql(fragment, out var sourceSql))
        {
            return sourceSql;
        }

        return GenerateSql(fragment);
    }

    private bool TryReadSourceSql(TSqlFragment fragment, out string sql)
    {
        sql = "";

        if (_sourceText is null
            || fragment.StartOffset < 0
            || fragment.FragmentLength <= 0
            || fragment.StartOffset + fragment.FragmentLength > _sourceText.Length)
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
            _ => false,
        };
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
        _lintFindings.Add(new LintFinding(rule, severity, _scope, sourceFile, LineOf(node), node.StartColumn, node.FragmentLength, message));
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

    private static int? SelectElementCount(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.SelectElements.Count,
        BinaryQueryExpression binary when SelectElementCount(binary.FirstQueryExpression) is { } first
            && SelectElementCount(binary.SecondQueryExpression) == first => first,
        _ => null,
    };

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
