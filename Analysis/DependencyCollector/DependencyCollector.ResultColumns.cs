using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    private void AddFunctionResultColumns(string objectName, FunctionStatementBody function)
    {
        if (function.ReturnType is SelectFunctionReturnType selectReturn)
        {
            AddSelectFunctionResultColumns(objectName, selectReturn);

            return;
        }

        if (function.ReturnType is TableValuedFunctionReturnType tableReturn)
        {
            AddTableFunctionResultColumns(objectName, tableReturn);
        }
    }

    private void AddSelectFunctionResultColumns(string objectName, SelectFunctionReturnType selectReturn)
    {
        if (GetSelectFunctionQuery(selectReturn) is not { } query)
        {
            return;
        }

        AddQueryResultColumns(objectName, query);
    }

    private static QuerySpecification? GetSelectFunctionQuery(SelectFunctionReturnType selectReturn)
    {
        if (selectReturn.SelectStatement is null)
        {
            return null;
        }

        return FirstQuerySpecification(selectReturn.SelectStatement.QueryExpression);
    }

    private void AddTableFunctionResultColumns(string objectName, TableValuedFunctionReturnType tableReturn)
    {
        if (tableReturn.DeclareTableVariableBody?.Definition is not { } definition)
        {
            return;
        }

        AddTableResultColumns(objectName, definition);
    }

    private void AddTableResultColumns(string objectName, TableDefinition definition)
    {
        for (var ordinal = 0; ordinal < definition.ColumnDefinitions.Count; ordinal++)
        {
            var column = definition.ColumnDefinitions[ordinal];
            var name = column.ColumnIdentifier.Value;
            _resultColumns.Add(new ResultColumn(objectName, ordinal, name, sourceFile, LineOf(column), true, GenerateSql(column.DataType)));
        }
    }

    private void AddStatementResultColumns(string objectName, StatementList? statementList)
    {
        if (!TryGetStatementResultQuery(statementList, out var query))
        {
            return;
        }

        AddQueryResultColumns(objectName, query);
    }

    private static bool TryGetStatementResultQuery(
        StatementList? statementList,
        out QuerySpecification query
    )
    {
        query = null!;
        if (statementList is null)
        {
            return false;
        }

        QuerySpecification? candidate = statementList.Statements
            .OfType<SelectStatement>()
            .Select(statement => FirstQuerySpecification(statement.QueryExpression))
            .FirstOrDefault(query => query is not null);
        if (candidate is null)
        {
            return false;
        }

        query = candidate;
        return true;
    }

    private void AddQueryResultColumns(string objectName, QuerySpecification query)
    {
        for (var ordinal = 0; ordinal < query.SelectElements.Count; ordinal++)
        {
            if (query.SelectElements[ordinal] is not SelectScalarExpression scalar)
            {
                continue;
            }

            var hasKnownName = TryResultColumnName(scalar, out var columnName);
            _resultColumns.Add(
                new ResultColumn(
                    objectName,
                    ordinal,
                    hasKnownName ? columnName : $"column{ordinal + 1}",
                    sourceFile,
                    LineOf(scalar),
                    hasKnownName,
                    ResultColumnType(scalar)
                )
            );
        }
    }

    /// <summary>Extracts a result type only when the SELECT expression states it explicitly.</summary>
    private string? ResultColumnType(SelectScalarExpression scalar) => scalar.Expression switch
    {
        CastCall cast => GenerateSql(cast.DataType),
        ConvertCall convert => GenerateSql(convert.DataType),
        _ => null,
    };

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
}
