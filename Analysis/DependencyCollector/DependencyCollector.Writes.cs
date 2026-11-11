using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Records DML targets and their written columns.</summary>
internal sealed partial class DependencyCollector
{
    private NamedTableReference? RecordWriteTarget(TableReference target, FromClause? fromClause, string operation)
    {
        if (target is not NamedTableReference named)
        {
            return null;
        }

        if (IsCommonTableExpression(named.SchemaObject))
        {
            AddLint("dml-cte-target", "warning", named, $"{operation} targets CTE '{named.SchemaObject.BaseIdentifier.Value}'; SQL Server updateability depends on the CTE shape.");

            return null;
        }

        if (IsDerivedTableTarget(named, fromClause))
        {
            AddLint("dml-derived-target", "warning", named, $"{operation} target '{named.SchemaObject.BaseIdentifier.Value}' resolves to a derived table, which is not a base table dependency.");

            return null;
        }

        var resolved = ResolveAlias(named, fromClause);
        _handled.Add(named);
        _handled.Add(resolved);
        AddTableDependency(resolved.SchemaObject, operation, resolved);

        return resolved;
    }

    private static bool IsDerivedTableTarget(NamedTableReference target, FromClause? fromClause)
    {
        if (fromClause is null)
        {
            return false;
        }

        return DirectQueryDerivedTableFinder.Find(fromClause)
            .Any(table => string.Equals(table.Alias?.Value, target.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));
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

        if (IsTransientTable(name))
        {
            return false;
        }

        return !IsCommonTableExpression(name);
    }

    private static bool IsTransientTable(SchemaObjectName name)
    {
        if (ObjectNames.IsTemporary(name))
        {
            return true;
        }

        return ObjectNames.IsTriggerPseudoTable(name);
    }

    private bool IsCommonTableExpression(SchemaObjectName name)
    {
        if (name.SchemaIdentifier is not null)
        {
            return false;
        }

        return cteNames.Contains(name.BaseIdentifier.Value);
    }

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
