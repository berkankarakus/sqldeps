using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>
/// Walks the parse tree of one batch and records which object does what to which object.
/// "source_object" is the procedure, function, view or trigger the statement sits in; statements
/// outside any of them belong to "(batch)".
/// </summary>
public sealed class DependencyCollector(
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

    private string DefineObject(TSqlStatement statement)
    {
        if (statement is ProcedureStatementBody procedure)
        {
            var name = Define(Format(procedure.ProcedureReference.Name), "procedure", statement);
            AddParameters(name, procedure.Parameters);

            return name;
        }

        if (statement is FunctionStatementBody function)
        {
            var name = Define(Format(function.Name), "function", statement);
            AddParameters(name, function.Parameters);

            return name;
        }

        if (statement is ViewStatementBody view)
        {
            var viewName = Define(Format(view.SchemaObjectName), "view", statement);
            AddViewColumns(viewName, view);
            return viewName;
        }

        if (statement is TriggerStatementBody trigger)
        {
            var triggerName = Define(Format(trigger.Name), "trigger", statement);

            if (trigger.TriggerObject is not null)
            {
                _scope = triggerName;
                AddDependency(Format(trigger.TriggerObject.Name), "ON", statement);
            }

            return triggerName;
        }

        if (statement is CreateTableStatement table && !ObjectNames.IsTemporary(table.SchemaObjectName))
        {
            var tableName = Define(Format(table.SchemaObjectName), "table", statement);
            AddTableColumns(tableName, table.Definition.ColumnDefinitions);
        }

        if (statement is AlterTableAddTableElementStatement addColumns && !ObjectNames.IsTemporary(addColumns.SchemaObjectName))
        {
            AddTableColumns(Format(addColumns.SchemaObjectName), addColumns.Definition.ColumnDefinitions);
        }

        if (statement is AlterTableAlterColumnStatement alterColumn && !ObjectNames.IsTemporary(alterColumn.SchemaObjectName))
        {
            AddTableColumn(Format(alterColumn.SchemaObjectName), alterColumn.ColumnIdentifier, alterColumn.DataType, alterColumn);
        }

        return LooseBatch;
    }

    private void AddTableColumns(string tableName, IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
        {
            AddTableColumn(tableName, column.ColumnIdentifier, column.DataType, column);
        }
    }

    private void AddTableColumn(string tableName, Identifier name, DataTypeReference? dataType, TSqlFragment node)
    {
        _tableColumns.Add(new TableColumn(tableName, name.Value, dataType is null ? "computed" : GenerateSql(dataType), sourceFile, LineOf(node)));
    }

    private string Define(
        string name,
        string kind,
        TSqlFragment node
    )
    {
        _objects.Add(new DatabaseObject(name, kind, sourceFile, LineOf(node)));

        return name;
    }

    private void AddViewColumns(string viewName, ViewStatementBody view)
    {
        if (view.SelectStatement.QueryExpression is not QuerySpecification query)
        {
            return;
        }

        var explicitNames = view.Columns.Select(column => column.Value).ToArray();
        for (var index = 0; index < query.SelectElements.Count; index++)
        {
            if (query.SelectElements[index] is not SelectScalarExpression scalar
                || !TryViewColumnName(scalar, out var inferredName))
            {
                continue;
            }

            var name = index < explicitNames.Length ? explicitNames[index] : inferredName;
            _tableColumns.Add(new TableColumn(viewName, name, "view column", sourceFile, LineOf(scalar)));
        }
    }

    private static bool TryViewColumnName(SelectScalarExpression scalar, out string name)
    {
        if (scalar.ColumnName is not null)
        {
            name = scalar.ColumnName.Value;
            return true;
        }

        if (scalar.Expression is ColumnReferenceExpression column && TryGetColumnName(column, out name))
        {
            return true;
        }

        name = "";
        return false;
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

    public override void Visit(QuerySpecification node)
    {
        var tables = TableReferenceFinder.Find(node.FromClause);

        foreach (var element in node.SelectElements.OfType<SelectScalarExpression>())
        {
            if (element.Expression is not ColumnReferenceExpression column
                || !TryResolveColumnTable(column, tables, out var table)
                || !TryGetColumnName(column, out var name))
            {
                continue;
            }

            AddColumnUsage(table, name, "SELECT", column);
        }
    }

    // ---- calls ----------------------------------------------------------------------------------

    public override void Visit(ExecutableProcedureReference node)
    {
        var procedureName = node.ProcedureReference is null ? null : node.ProcedureReference.ProcedureReference;

        // EXEC @procedure, or a name that is not known until run time.
        if (procedureName is null || procedureName.Name is null)
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", node);

            return;
        }

        if (IsDynamicSqlProcedure(procedureName.Name))
        {
            CollectDynamicSql(FirstParameterOf(node), node);

            return;
        }

        AddDependency(Format(procedureName.Name), "EXEC", node);
    }

    public override void Visit(ExecutableStringList node)
    {
        CollectDynamicSql(node.Strings, node);
    }

    public override void Visit(FunctionCall node)
    {
        MultiPartIdentifierCallTarget? target = node.CallTarget as MultiPartIdentifierCallTarget;

        if (target is null)
        {
            return;
        }

        if (XmlMethods.Contains(node.FunctionName.Value))
        {
            return;
        }

        var identifiers = target.MultiPartIdentifier.Identifiers;
        var schema = identifiers[identifiers.Count - 1].Value;
        AddDependency(schema + "." + node.FunctionName.Value, "CALL", node);
    }

    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        AddDependency(Format(node.SchemaObject), "CALL", node);
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Records the table an INSERT, UPDATE, DELETE or MERGE writes. In UPDATE a SET ... FROM dbo.Prices p
    /// the target is the alias "p"; the table it stands for is the one in the FROM clause.
    /// </summary>
    private NamedTableReference? RecordWriteTarget(
        TableReference target,
        FromClause? fromClause,
        string operation
    )
    {
        NamedTableReference? named = target as NamedTableReference;

        if (named is null)
        {
            return null;
        }

        var resolved = ResolveAlias(named, fromClause);
        _handled.Add(named);
        _handled.Add(resolved);
        AddTableDependency(resolved.SchemaObject, operation, resolved);

        return resolved;
    }

    private void RecordInsertColumns(
        NamedTableReference? target,
        IList<ColumnReferenceExpression> columns
    )
    {
        if (target is null)
        {
            return;
        }

        foreach (var column in columns)
        {
            if (TryGetColumnName(column, out var name))
            {
                AddColumnUsage(target, name, "INSERT", column);
            }
        }
    }

    private void RecordUpdateColumns(
        NamedTableReference? target,
        IList<SetClause> clauses
    )
    {
        if (target is null)
        {
            return;
        }

        foreach (var assignment in clauses.OfType<AssignmentSetClause>())
        {
            if (assignment.Column is { } column && TryGetColumnName(column, out var name))
            {
                AddColumnUsage(target, name, "UPDATE", column);
            }
        }
    }

    private void AddColumnUsage(
        NamedTableReference table,
        string column,
        string operation,
        TSqlFragment at
    )
    {
        if (ObjectNames.IsTemporary(table.SchemaObject)
            || ObjectNames.IsTriggerPseudoTable(table.SchemaObject)
            || (table.SchemaObject.SchemaIdentifier is null && cteNames.Contains(table.SchemaObject.BaseIdentifier.Value)))
        {
            return;
        }

        _columnUsage.Add(new ColumnUsage(_scope, Format(table.SchemaObject), column, operation, sourceFile, LineOf(at)));
    }

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

    private static NamedTableReference ResolveAlias(
        NamedTableReference target,
        FromClause? fromClause
    )
    {
        var writtenWithoutSchema = target.SchemaObject.SchemaIdentifier is null;

        if (!writtenWithoutSchema)
        {
            return target;
        }

        foreach (var reference in TableReferenceFinder.Find(fromClause))
        {
            var alias = reference.Alias is null ? null : reference.Alias.Value;

            if (string.Equals(alias, target.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase))
            {
                return reference;
            }
        }

        return target;
    }

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

    private void CollectDynamicSql(
        IEnumerable<ValueExpression>? expressions,
        TSqlFragment at
    )
    {
        if (expressions is null || !TryReadConstantSql(expressions, out var sql, out var firstLiteral))
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

            return;
        }

        CollectDynamicSql(sql, firstLiteral, at);
    }

    private void CollectDynamicSql(
        string sql,
        StringLiteral firstLiteral,
        TSqlFragment at
    )
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;

        using (StringReader reader = new(sql))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0)
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

            return;
        }

        if (fragment is not TSqlScript script)
        {
            return;
        }

        var nested = new DependencyCollector(sourceFile, LineOf(firstLiteral), CteNameCollector.Collect(script), defaultSchema);
        nested.CollectInside(script, _scope);
        _dependencies.AddRange(nested.Dependencies.Select(dependency => dependency with { ViaDynamic = true }));
        _columnUsage.AddRange(nested.ColumnUsage);
        _tableColumns.AddRange(nested.TableColumns);
        _problems.AddRange(nested.Problems);
        _lintFindings.AddRange(nested.LintFindings);
    }

    private void CollectDynamicSql(
        ScalarExpression? expression,
        TSqlFragment at
    )
    {
        if (!TryReadConstantSql(expression, out var sql, out var firstLiteral))
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

            return;
        }

        CollectDynamicSql(sql, firstLiteral, at);
    }

    private void CollectInside(
        TSqlScript script,
        string enclosingScope
    )
    {
        _scope = enclosingScope;

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                statement.Accept(this);
            }
        }
    }

    private static ScalarExpression? FirstParameterOf(ExecutableProcedureReference node)
    {
        return node.Parameters.Count == 0 ? null : node.Parameters[0].ParameterValue;
    }

    private static bool TryReadConstantSql(
        IEnumerable<ValueExpression> expressions,
        out string sql,
        out StringLiteral firstLiteral
    )
    {
        var values = expressions.ToList();
        var literals = values.OfType<StringLiteral>().ToList();

        if (literals.Count == 0 || literals.Count != values.Count)
        {
            sql = "";
            firstLiteral = null!;

            return false;
        }

        sql = string.Concat(literals.Select(literal => literal.Value));
        firstLiteral = literals[0];

        return true;
    }

    private static bool TryReadConstantSql(
        ScalarExpression? expression,
        out string sql,
        out StringLiteral firstLiteral
    )
    {
        if (expression is StringLiteral literal)
        {
            sql = literal.Value;
            firstLiteral = literal;

            return true;
        }

        if (expression is BinaryExpression
            {
                BinaryExpressionType: BinaryExpressionType.Add or BinaryExpressionType.Concat
            } binary
            && TryReadConstantSql(binary.FirstExpression, out var first, out firstLiteral)
            && TryReadConstantSql(binary.SecondExpression, out var second, out _))
        {
            sql = first + second;

            return true;
        }

        sql = "";
        firstLiteral = null!;

        return false;
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

    private static bool IsDynamicSqlProcedure(SchemaObjectName name)
    {
        return string.Equals(name.BaseIdentifier.Value, "sp_executesql", StringComparison.OrdinalIgnoreCase);
    }
}
