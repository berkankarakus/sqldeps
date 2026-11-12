using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(InsertStatement node)
    {
        ReportInsertSelectFilter(node);

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
        if (ReportAlwaysFalseCteSource(
            specification.FromClause,
            statement.WithCtesAndXmlNamespaces,
            statement)
            || ReportAlwaysFalseInnerJoin(specification.FromClause, statement))
        {
            return;
        }

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

        if (ReportAlwaysFalseCteSource(
            specification.FromClause,
            statement.WithCtesAndXmlNamespaces,
            statement)
            || ReportAlwaysFalseInnerJoin(specification.FromClause, statement))
        {
            return;
        }

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

    private void ReportInsertSelectFilter(InsertStatement statement)
    {
        if (statement.InsertSpecification.InsertSource is not SelectInsertSource
            {
                Select: QuerySpecification query
            })
        {
            return;
        }

        _insertSelectQueries.Add(query);

        if (ReportAlwaysFalseCteSource(
            query.FromClause,
            statement.WithCtesAndXmlNamespaces,
            statement)
            || ReportAlwaysFalseInnerJoin(query.FromClause, statement))
        {
            return;
        }

        if (query.WhereClause?.SearchCondition is { } whereCondition)
        {
            ReportConstantDmlFilter(whereCondition, statement);
        }

        if (query.HavingClause?.SearchCondition is { } havingCondition)
        {
            ReportConstantDmlFilter(havingCondition, statement);
        }
    }

    private bool ReportAlwaysFalseCteSource(
        FromClause? fromClause,
        WithCtesAndXmlNamespaces? withCtes,
        TSqlStatement statement
    )
    {
        if (fromClause is null || withCtes is null)
        {
            return false;
        }

        foreach (var table in fromClause.TableReferences.OfType<NamedTableReference>())
        {
            if (table.SchemaObject.SchemaIdentifier is not null)
            {
                continue;
            }

            var cte = withCtes.CommonTableExpressions.FirstOrDefault(expression =>
                string.Equals(
                    expression.ExpressionName.Value,
                    table.SchemaObject.BaseIdentifier.Value,
                    StringComparison.OrdinalIgnoreCase));

            if (cte is null || !TryFindAlwaysFalseInnerJoin(cte.QueryExpression, out var condition))
            {
                continue;
            }

            _dmlFalseInnerJoinConditions.Add(condition);
            AddLint(
                "dml-always-false-filter",
                "info",
                statement,
                "DML source CTE contains an inner JOIN condition that is always false and will not produce rows."
            );

            return true;
        }

        return false;
    }

    private bool ReportAlwaysFalseInnerJoin(FromClause? fromClause, TSqlStatement statement)
    {
        if (!TryFindAlwaysFalseInnerJoin(fromClause, out var condition))
        {
            return false;
        }

        _dmlFalseInnerJoinConditions.Add(condition);

        AddLint(
            "dml-always-false-filter",
            "info",
            statement,
            "DML inner JOIN condition is always false and will not affect any rows."
        );

        return true;
    }

    private bool TryFindAlwaysFalseInnerJoin(
        FromClause? fromClause,
        out BooleanExpression condition
    )
    {
        condition = null!;

        if (fromClause is null)
        {
            return false;
        }

        foreach (var table in fromClause.TableReferences)
        {
            if (TryFindAlwaysFalseInnerJoin(table, out condition))
            {
                return true;
            }

            if (table is QueryDerivedTable derived
                && TryFindAlwaysFalseInnerJoin(derived.QueryExpression, out condition))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindAlwaysFalseInnerJoin(
        QueryExpression query,
        out BooleanExpression condition
    )
    {
        condition = null!;

        if (query is QuerySpecification specification)
        {
            return TryFindAlwaysFalseInnerJoin(specification.FromClause, out condition);
        }

        if (query is QueryParenthesisExpression parenthesized)
        {
            return TryFindAlwaysFalseInnerJoin(parenthesized.QueryExpression, out condition);
        }

        if (query is BinaryQueryExpression binary)
        {
            if (TryFindAlwaysFalseInnerJoin(binary.FirstQueryExpression, out condition))
            {
                return true;
            }

            return TryFindAlwaysFalseInnerJoin(binary.SecondQueryExpression, out condition);
        }

        return false;
    }

    private bool TryFindAlwaysFalseInnerJoin(
        TableReference table,
        out BooleanExpression condition
    )
    {
        condition = null!;

        if (table is QualifiedJoin join)
        {
            if (join.QualifiedJoinType == QualifiedJoinType.Inner
                && join.SearchCondition is { } searchCondition
                && EvaluateCondition(searchCondition) == false)
            {
                condition = searchCondition;

                return true;
            }

            if (TryFindAlwaysFalseInnerJoin(join.FirstTableReference, out condition))
            {
                return true;
            }

            return TryFindAlwaysFalseInnerJoin(join.SecondTableReference, out condition);
        }

        if (table is JoinTableReference joinTable)
        {
            if (TryFindAlwaysFalseInnerJoin(joinTable.FirstTableReference, out condition))
            {
                return true;
            }

            return TryFindAlwaysFalseInnerJoin(joinTable.SecondTableReference, out condition);
        }

        return false;
    }

}
