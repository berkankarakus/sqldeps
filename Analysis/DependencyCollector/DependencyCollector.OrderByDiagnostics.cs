using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    private void ReportDistinctOrderBy(QuerySpecification query)
    {
        if (query.UniqueRowFilter != UniqueRowFilter.Distinct || query.OrderByClause is null)
        {
            return;
        }

        HashSet<string> selectedNames = query.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(SelectedColumnName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedAliases = query.SelectElements
            .OfType<SelectScalarExpression>()
            .Where(element => element.ColumnName is not null)
            .Select(element => element.ColumnName!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in query.OrderByClause.OrderByElements
            .Select(element => element.Expression)
            .OfType<ColumnReferenceExpression>())
        {
            if (!IsMissingDistinctOrderByColumn(column, selectedNames, selectedAliases))
            {
                continue;
            }

            string columnName = column.MultiPartIdentifier!.Identifiers[0].Value;
            AddLint(
                "distinct-order-by",
                "error",
                column,
                $"ORDER BY column '{columnName}' must appear in the SELECT list when DISTINCT is used.");
        }
    }

    private static string? SelectedColumnName(SelectScalarExpression element)
    {
        if (element.Expression is not ColumnReferenceExpression column)
        {
            return null;
        }

        return column.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
    }

    private static bool IsMissingDistinctOrderByColumn(
        ColumnReferenceExpression column,
        IReadOnlySet<string> selectedNames,
        IReadOnlySet<string> selectedAliases)
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers?.Count != 1)
        {
            return false;
        }

        string columnName = identifiers[0].Value;

        return !selectedNames.Contains(columnName) && !selectedAliases.Contains(columnName);
    }
}
