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
    string defaultSchema
) : TSqlFragmentVisitor
{
    private const string LooseBatch = "(batch)";

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
    private readonly List<ColumnUsage> _columnUsage = [];
    private readonly List<TableColumn> _tableColumns = [];
    private readonly List<ParseProblem> _problems = [];
    private readonly List<LintFinding> _lintFindings = [];
    private string _scope = LooseBatch;

    // Table references already recorded as the target of a write, so the walk does not also count them as reads.
    private readonly HashSet<TSqlFragment> _handled = [];

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => _parameters;

    public IReadOnlyList<ColumnUsage> ColumnUsage => _columnUsage;
    public IReadOnlyList<TableColumn> TableColumns => _tableColumns;

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
        var target = RecordWriteTarget(specification.Target, specification.FromClause, "UPDATE");
        RecordUpdateColumns(target, specification.SetClauses);
    }

    public override void Visit(DeleteStatement node)
    {
        if (node.DeleteSpecification.WhereClause is null)
        {
            AddLint("delete-without-where", "warning", node, "DELETE has no WHERE clause and can remove every matching row.");
        }
        RecordWriteTarget(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause, "DELETE");
    }

    public override void Visit(SelectStarExpression node)
    {
        AddLint("select-star", "info", node, "SELECT * makes result shape and schema changes harder to reason about.");
    }

    public override void Visit(BooleanComparisonExpression node)
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

    public override void Visit(LikePredicate node)
    {
        if (node.SecondExpression is not StringLiteral literal)
        {
            return;
        }

        if (!literal.Value.StartsWith('%'))
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

    public override void Visit(TruncateTableStatement node)
    {
        AddTableDependency(node.TableName, "TRUNCATE", node);
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
        ReportDuplicateAliases(tables);
        ReportUngroupedSelectColumns(node);

        foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
        {
            if (element.Expression is not ColumnReferenceExpression column)
            {
                continue;
            }

            ValidateAliasReference(column, tables);

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

    private void ReportUngroupedSelectColumns(QuerySpecification query)
    {
        if (query.GroupByClause is null)
        {
            return;
        }

        var groupedColumns = query.GroupByClause.GroupingSpecifications
            .OfType<ExpressionGroupingSpecification>()
            .Select(specification => specification.Expression)
            .OfType<ColumnReferenceExpression>()
            .Select(ColumnReferenceText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in query.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(element => element.Expression)
            .OfType<ColumnReferenceExpression>())
        {
            var name = ColumnReferenceText(column);
            if (!groupedColumns.Contains(name))
            {
                AddLint("ungrouped-select-column", "error", column, $"Column '{name}' appears in SELECT but is not included in GROUP BY or an aggregate.");
            }
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

    private void ValidateAliasReference(ColumnReferenceExpression column, IReadOnlyList<NamedTableReference> tables)
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count < 2)
        {
            return;
        }

        var alias = identifiers[0].Value;
        var aliasIsKnown = IsKnownAlias(tables, alias);
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

        if (identifiers.Count < 2)
        {
            table = null!;

            return false;
        }

        var qualifier = identifiers[identifiers.Count - 2].Value;
        NamedTableReference? match = null;

        foreach (var reference in tables)
        {
            var matches = string.Equals(reference.Alias?.Value, qualifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference.SchemaObject.BaseIdentifier.Value, qualifier, StringComparison.OrdinalIgnoreCase);

            if (!matches)
            {
                continue;
            }

            if (match is not null)
            {
                table = null!;

                return false;
            }

            match = reference;
        }

        if (match is null)
        {
            table = null!;

            return false;
        }

        table = match;

        return true;
    }

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

    private static string ColumnReferenceText(ColumnReferenceExpression column) => string.Join('.', column.MultiPartIdentifier.Identifiers.Select(identifier => identifier.Value));

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

    private static string GenerateSql(TSqlFragment? fragment)
    {
        if (fragment is null)
        {
            return "unknown";
        }

        var generator = new Sql160ScriptGenerator();
        generator.GenerateScript(fragment, out var sql);

        return sql.Trim();
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

}
