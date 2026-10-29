using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Records DML targets and their written columns.</summary>
public sealed partial class DependencyCollector
{
    private NamedTableReference? RecordWriteTarget(TableReference target, FromClause? fromClause, string operation)
    {
        if (target is not NamedTableReference named)
        {
            return null;
        }

        var resolved = ResolveAlias(named, fromClause);
        _handled.Add(named);
        _handled.Add(resolved);
        AddTableDependency(resolved.SchemaObject, operation, resolved);

        return resolved;
    }

    private void RecordInsertColumns(NamedTableReference? target, IList<ColumnReferenceExpression> columns)
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

    private void RecordUpdateColumns(NamedTableReference? target, IList<SetClause> clauses)
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

    private void AddColumnUsage(NamedTableReference table, string column, string operation, TSqlFragment at)
    {
        if (!ShouldTrackColumnUsage(table))
        {
            return;
        }

        _columnUsage.Add(new ColumnUsage(_scope, Format(table.SchemaObject), column, operation, sourceFile, LineOf(at)));
    }

    private bool ShouldTrackColumnUsage(NamedTableReference table)
    {
        var name = table.SchemaObject;

        if (ObjectNames.IsTemporary(name) || ObjectNames.IsTriggerPseudoTable(name))
        {
            return false;
        }

        return !IsCommonTableExpression(name);
    }

    private bool IsCommonTableExpression(SchemaObjectName name) => name.SchemaIdentifier is null
        && cteNames.Contains(name.BaseIdentifier.Value);

    private static NamedTableReference ResolveAlias(NamedTableReference target, FromClause? fromClause)
    {
        if (target.SchemaObject.SchemaIdentifier is not null)
        {
            return target;
        }

        return TableReferenceFinder.Find(fromClause).FirstOrDefault(reference =>
            string.Equals(reference.Alias?.Value, target.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase)) ?? target;
    }
}
