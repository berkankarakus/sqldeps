using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    private string AddTableColumns(AlterTableAddTableElementStatement statement)
    {
        if (!ObjectNames.IsTemporary(statement.SchemaObjectName))
        {
            var tableName = Format(statement.SchemaObjectName);
            AddTableColumns(tableName, statement.Definition.ColumnDefinitions);
            AddTableKeys(tableName, statement.Definition.TableConstraints);
            AddForeignKeys(tableName, statement.Definition.TableConstraints);
        }

        return LooseBatch;
    }

    private string AlterColumn(AlterTableAlterColumnStatement statement)
    {
        if (!ObjectNames.IsTemporary(statement.SchemaObjectName))
        {
            AddTableColumn(
                Format(statement.SchemaObjectName),
                statement.ColumnIdentifier,
                statement.DataType,
                statement,
                statement.AlterTableAlterColumnOption switch
                {
                    AlterTableAlterColumnOption.Null => true,
                    AlterTableAlterColumnOption.NotNull => false,
                    _ => null,
                },
                statement.Collation?.Value);
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

    private void AddTableKeys(string tableName, IEnumerable<ConstraintDefinition> constraints)
    {
        foreach (var constraint in constraints.OfType<UniqueConstraintDefinition>())
        {
            var kind = constraint.IsPrimaryKey ? "primary-key" : "unique";
            var constraintName = constraint.ConstraintIdentifier?.Value ?? "";

            for (var ordinal = 0; ordinal < constraint.Columns.Count; ordinal++)
            {
                _tableKeys.Add(new TableKey(
                    tableName,
                    kind,
                    constraintName,
                    constraint.Columns[ordinal].Column.MultiPartIdentifier.Identifiers[^1].Value,
                    ordinal,
                    sourceFile,
                    LineOf(constraint.Columns[ordinal])));
            }
        }
    }

    private void AddForeignKeys(string tableName, IEnumerable<ConstraintDefinition> constraints)
    {
        foreach (var constraint in constraints.OfType<ForeignKeyConstraintDefinition>())
        {
            var constraintName = constraint.ConstraintIdentifier?.Value ?? "";
            var referencedTable = Format(constraint.ReferenceTableName);

            for (var ordinal = 0; ordinal < constraint.Columns.Count; ordinal++)
            {
                if (ordinal >= constraint.ReferencedTableColumns.Count)
                {
                    continue;
                }

                _foreignKeys.Add(new TableForeignKey(
                    tableName,
                    constraintName,
                    constraint.Columns[ordinal].Value,
                    referencedTable,
                    constraint.ReferencedTableColumns[ordinal].Value,
                    ordinal,
                    sourceFile,
                    LineOf(constraint)));
            }
        }
    }

    private void AddTableColumn(
        string tableName,
        Identifier name,
        DataTypeReference? dataType,
        TSqlFragment node,
        bool? nullableOverride = null,
        string? collation = null)
    {
        var definition = node as ColumnDefinition;
        var nullable = nullableOverride ?? definition?.Constraints
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
            collation ?? definition?.Collation?.Value
        ));
    }

    private static int? IntegerParameter(IList<Literal>? parameters, int index)
    {
        if (parameters is null || parameters.Count <= index)
        {
            return null;
        }

        if (parameters[index] is not IntegerLiteral literal)
        {
            return null;
        }

        return int.TryParse(literal.Value, out var value) ? value : null;
    }
}
