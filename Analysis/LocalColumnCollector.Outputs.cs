using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class LocalColumnCollector
{
    private IEnumerable<string> OutputColumns(QueryExpression query)
    {
        if (query is BinaryQueryExpression binary)
        {
            return OutputColumns(binary.FirstQueryExpression);
        }

        if (query is QueryParenthesisExpression parenthesized)
        {
            return OutputColumns(parenthesized.QueryExpression);
        }

        if (query is not QuerySpecification specification)
        {
            return [];
        }

        return specification.SelectElements.SelectMany(element => OutputColumns(element, specification));
    }

    private IEnumerable<string> OutputColumns(SelectElement element, QuerySpecification specification)
    {
        if (element is SelectScalarExpression scalar)
        {
            return ColumnName(scalar) is { } name ? [name] : [];
        }

        if (element is not SelectStarExpression star)
        {
            return [];
        }

        var qualifier = star.Qualifier?.Identifiers.LastOrDefault()?.Value;

        return ColumnsFromSources(specification.FromClause, qualifier);
    }

    private static bool MatchesQualifier(string? qualifier, string? alias, string name)
    {
        return qualifier is null
            || string.Equals(qualifier, alias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(qualifier, name, StringComparison.OrdinalIgnoreCase);
    }

    private List<string> LocalColumnsFor(string alias)
    {
        return _columns
            .Where(column => string.Equals(column.Alias, alias, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ColumnName(SelectScalarExpression element)
    {
        if (element.ColumnName is not null)
        {
            return element.ColumnName.Value;
        }

        return element.Expression is ColumnReferenceExpression column
            ? column.MultiPartIdentifier.Identifiers.LastOrDefault()?.Value
            : null;
    }
}
