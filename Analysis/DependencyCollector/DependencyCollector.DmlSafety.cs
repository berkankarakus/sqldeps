using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(InsertStatement node)
    {
        var target = RecordWriteTarget(node.InsertSpecification.Target, null, "INSERT");
        RecordInsertColumns(target, node.InsertSpecification.Columns);
    }

    public override void Visit(UpdateStatement node)
    {
        var specification = node.UpdateSpecification;
        ReportUpdateSafety(node, specification);

        var target = RecordWriteTarget(specification.Target, specification.FromClause, "UPDATE");
        RecordUpdateColumns(target, specification.SetClauses);
    }

    public override void Visit(DeleteStatement node)
    {
        ReportDeleteSafety(node);

        RecordWriteTarget(node.DeleteSpecification.Target, node.DeleteSpecification.FromClause, "DELETE");
    }

    private void ReportUpdateSafety(UpdateStatement statement, UpdateSpecification specification)
    {
        if (specification.WhereClause is not null)
        {
            ReportConstantDmlFilter(specification.WhereClause.SearchCondition, statement);

            return;
        }

        if (IsUpdateSafetyExempt(statement, specification))
        {
            return;
        }

        AddLint("update-without-where", "warning", statement, "UPDATE has no WHERE clause and can modify every matching row.");
    }

    private void ReportDeleteSafety(DeleteStatement statement)
    {
        var specification = statement.DeleteSpecification;

        if (specification.WhereClause is not null)
        {
            ReportConstantDmlFilter(specification.WhereClause.SearchCondition, statement);

            return;
        }

        if (IsDeleteSafetyExempt(statement, specification))
        {
            return;
        }

        AddLint("delete-without-where", "warning", statement, "DELETE has no WHERE clause and can remove every matching row.");
    }

}
