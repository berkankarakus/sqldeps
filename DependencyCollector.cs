using Microsoft.SqlServer.TransactSql.ScriptDom;

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
    private static readonly HashSet<string> XmlMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "value",
        "query",
        "exist",
        "nodes",
        "modify"
    };

    private readonly List<DatabaseObject> _objects = new List<DatabaseObject>();
    private readonly List<Dependency> _dependencies = new List<Dependency>();
    private string _scope = LooseBatch;

    // Table references already recorded as the target of a write, so the walk does not also count them as reads.
    private readonly HashSet<TSqlFragment> _handled = new HashSet<TSqlFragment>();

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

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
            return Define(Format(procedure.ProcedureReference.Name), "procedure", statement);
        }

        if (statement is FunctionStatementBody function)
        {
            return Define(Format(function.Name), "function", statement);
        }

        if (statement is ViewStatementBody view)
        {
            return Define(Format(view.SchemaObjectName), "view", statement);
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
            Define(Format(table.SchemaObjectName), "table", statement);
        }

        return LooseBatch;
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

    // ---- reads and writes -----------------------------------------------------------------------

    public override void Visit(NamedTableReference node)
    {
        if (_handled.Contains(node))
        {
            return;
        }

        AddTableDependency(node.SchemaObject, "SELECT", node);
    }

    public override void Visit(InsertStatement node)
    {
        RecordWriteTarget(node.InsertSpecification.Target, null, "INSERT");
    }

    public override void Visit(UpdateStatement node)
    {
        RecordWriteTarget(node.UpdateSpecification.Target, node.UpdateSpecification.FromClause, "UPDATE");
    }

    public override void Visit(DeleteStatement node)
    {
        RecordWriteTarget(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause, "DELETE");
    }

    public override void Visit(MergeStatement node)
    {
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
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", node);

            return;
        }

        AddDependency(Format(procedureName.Name), "EXEC", node);
    }

    public override void Visit(ExecutableStringList node)
    {
        AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", node);
    }

    public override void Visit(FunctionCall node)
    {
        var target = node.CallTarget as MultiPartIdentifierCallTarget;

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
    private void RecordWriteTarget(
        TableReference target,
        FromClause? fromClause,
        string operation
    )
    {
        var named = target as NamedTableReference;

        if (named is null)
        {
            return;
        }

        var resolved = ResolveAlias(named, fromClause);
        _handled.Add(named);
        _handled.Add(resolved);
        AddTableDependency(resolved.SchemaObject, operation, resolved);
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

    private string Format(SchemaObjectName name)
    {
        return ObjectNames.Format(name, defaultSchema);
    }

    private int LineOf(TSqlFragment fragment)
    {
        return batchStartLine + fragment.StartLine - 1;
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
