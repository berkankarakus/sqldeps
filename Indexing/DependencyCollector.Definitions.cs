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
        AlterTableAddTableElementStatement addColumns => AddTableColumns(addColumns),
        AlterTableAlterColumnStatement alterColumn => AlterColumn(alterColumn),
        _ => LooseBatch,
    };

    private string DefineProcedure(ProcedureStatementBody procedure, TSqlStatement statement)
    {
        var name = Define(Format(procedure.ProcedureReference.Name), "procedure", statement);
        AddParameters(name, procedure.Parameters);
        AddResultColumns(name, procedure.StatementList);

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

    private void AddResultColumns(string objectName, StatementList statementList)
    {
        var query = statementList.Statements
            .OfType<SelectStatement>()
            .Select(statement => FirstQuerySpecification(statement.QueryExpression))
            .FirstOrDefault(query => query is not null);

        if (query is null)
        {
            return;
        }

        for (var ordinal = 0; ordinal < query.SelectElements.Count; ordinal++)
        {
            if (query.SelectElements[ordinal] is not SelectScalarExpression scalar
                || !TryResultColumnName(scalar, out var columnName))
            {
                continue;
            }

            _resultColumns.Add(new ResultColumn(objectName, ordinal, columnName, sourceFile, LineOf(scalar)));
        }
    }

    private static QuerySpecification? FirstQuerySpecification(QueryExpression query) => query switch
    {
        QuerySpecification specification => specification,
        QueryParenthesisExpression parenthesized => FirstQuerySpecification(parenthesized.QueryExpression),
        BinaryQueryExpression binary => FirstQuerySpecification(binary.FirstQueryExpression),
        _ => null,
    };

    private static bool TryResultColumnName(SelectScalarExpression scalar, out string name)
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

    private string AddTableColumns(AlterTableAddTableElementStatement statement)
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

    private void AddTableColumn(string tableName, Identifier name, DataTypeReference? dataType, TSqlFragment node)
    {
        var definition = node as ColumnDefinition;
        var nullable = definition?.Constraints
            .OfType<NullableConstraintDefinition>()
            .Select(constraint => (bool?)constraint.Nullable)
            .FirstOrDefault();
        var defaultValue = definition?.DefaultConstraint is { Expression: { } expression }
            ? GenerateSourceOrSql(expression)
            : null;
        var computed = definition?.ComputedColumnExpression is not null || dataType is null;
        var parameters = (dataType as ParameterizedDataTypeReference)?.Parameters;

        _tableColumns.Add(new TableColumn(
            tableName,
            name.Value,
            dataType is null ? "computed" : GenerateSql(dataType),
            sourceFile,
            LineOf(node),
            nullable,
            definition?.IdentityOptions is not null,
            defaultValue,
            computed,
            IntegerParameter(parameters, 0),
            IntegerParameter(parameters, 1),
            definition?.Collation?.Value
        ));
    }

    private static int? IntegerParameter(IList<Literal>? parameters, int index)
    {
        return parameters is not null
            && parameters.Count > index
            && parameters[index] is IntegerLiteral literal
            && int.TryParse(literal.Value, out var value)
                ? value
                : null;
    }

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
            var scalar = query.SelectElements[index] as SelectScalarExpression;

            if (scalar is null)
            {
                continue;
            }

            if (!TryViewColumnName(scalar, out var inferredName))
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
