using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Records objects and schemas declared by a SQL statement.</summary>
public sealed partial class DependencyCollector
{
    private string DefineObject(TSqlStatement statement) => statement switch
    {
        ProcedureStatementBody procedure => DefineProcedure(procedure, statement),
        FunctionStatementBody function => DefineFunction(function, statement),
        ViewStatementBody view => DefineView(view, statement),
        TriggerStatementBody trigger => DefineTrigger(trigger, statement),
        CreateTableStatement table => DefineTable(table, statement),
        AlterTableAddTableElementStatement addColumns => AddTableColumns(addColumns, statement),
        AlterTableAlterColumnStatement alterColumn => AlterColumn(alterColumn),
        _ => LooseBatch,
    };

    private string DefineProcedure(ProcedureStatementBody procedure, TSqlStatement statement)
    {
        var name = Define(Format(procedure.ProcedureReference.Name), "procedure", statement);
        AddParameters(name, procedure.Parameters);

        return name;
    }

    private string DefineFunction(FunctionStatementBody function, TSqlStatement statement)
    {
        var name = Define(Format(function.Name), "function", statement);
        AddParameters(name, function.Parameters);

        return name;
    }

    private string DefineView(ViewStatementBody view, TSqlStatement statement)
    {
        var name = Define(Format(view.SchemaObjectName), "view", statement);
        AddViewColumns(name, view);

        return name;
    }

    private string DefineTrigger(TriggerStatementBody trigger, TSqlStatement statement)
    {
        var name = Define(Format(trigger.Name), "trigger", statement);

        if (trigger.TriggerObject is not null)
        {
            _scope = name;
            AddDependency(Format(trigger.TriggerObject.Name), "ON", statement);
        }

        return name;
    }

    private string DefineTable(CreateTableStatement table, TSqlStatement statement)
    {
        if (ObjectNames.IsTemporary(table.SchemaObjectName))
        {
            return LooseBatch;
        }

        var name = Define(Format(table.SchemaObjectName), "table", statement);
        AddTableColumns(name, table.Definition.ColumnDefinitions);

        return LooseBatch;
    }

    private string AddTableColumns(AlterTableAddTableElementStatement statement, TSqlStatement node)
    {
        if (!ObjectNames.IsTemporary(statement.SchemaObjectName))
        {
            AddTableColumns(Format(statement.SchemaObjectName), statement.Definition.ColumnDefinitions);
        }

        return LooseBatch;
    }

    private string AlterColumn(AlterTableAlterColumnStatement statement)
    {
        if (!ObjectNames.IsTemporary(statement.SchemaObjectName))
        {
            AddTableColumn(Format(statement.SchemaObjectName), statement.ColumnIdentifier, statement.DataType, statement);
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

    private void AddTableColumn(string tableName, Identifier name, DataTypeReference? dataType, TSqlFragment node) => _tableColumns.Add(new TableColumn(tableName, name.Value, dataType is null ? "computed" : GenerateSql(dataType), sourceFile, LineOf(node)));

    private string Define(string name, string kind, TSqlFragment node)
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
            if (query.SelectElements[index] is not SelectScalarExpression scalar || !TryViewColumnName(scalar, out var inferredName))
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
}
