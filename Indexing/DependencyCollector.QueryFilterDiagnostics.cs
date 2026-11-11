using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    private void ReportUnfilteredQuery(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        var alwaysFalseFilter = AlwaysFalseFilter(query);

        if (alwaysFalseFilter is not null)
        {
            AddLint(
                "select-always-false-filter",
                "info",
                query,
                $"SELECT {alwaysFalseFilter} condition is always false and will return no rows."
            );

            return;
        }

        var alwaysTrueFilter = AlwaysTrueFilter(query);

        if (alwaysTrueFilter is not null)
        {
            AddLint(
                "select-ineffective-filter",
                "info",
                query,
                $"SELECT {alwaysTrueFilter} condition is always true and does not restrict the result set."
            );

            return;
        }

        if (HasQueryRestriction(query, tables))
        {
            return;
        }

        AddLint(
            "unfiltered-select",
            "info",
            query,
            "SELECT has no WHERE or TOP filter and may read many rows; verify that a broad result set is intentional."
        );
    }

    private static string? AlwaysFalseFilter(QuerySpecification query)
    {
        if (query.WhereClause?.SearchCondition is { } whereCondition
            && ConstantExpressionEvaluator.Evaluate(whereCondition) == false)
        {
            return "WHERE";
        }

        if (query.HavingClause?.SearchCondition is { } havingCondition
            && ConstantExpressionEvaluator.Evaluate(havingCondition) == false)
        {
            return "HAVING";
        }

        return null;
    }

    private static string? AlwaysTrueFilter(QuerySpecification query)
    {
        if (query.WhereClause?.SearchCondition is { } whereCondition
            && ConstantExpressionEvaluator.Evaluate(whereCondition) == true)
        {
            return "WHERE";
        }

        if (query.HavingClause?.SearchCondition is { } havingCondition
            && ConstantExpressionEvaluator.Evaluate(havingCondition) == true)
        {
            return "HAVING";
        }

        return null;
    }

    private static bool HasQueryRestriction(QuerySpecification query, IReadOnlyList<NamedTableReference> tables)
    {
        if (query.WhereClause is not null)
        {
            return true;
        }

        if (query.TopRowFilter is not null)
        {
            return true;
        }

        return tables.Count == 0;
    }
}
