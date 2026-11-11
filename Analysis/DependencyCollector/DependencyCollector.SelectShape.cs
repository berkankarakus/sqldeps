using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(BinaryQueryExpression node)
    {
        var firstCount = SelectElementCount(node.FirstQueryExpression);
        var secondCount = SelectElementCount(node.SecondQueryExpression);

        if (firstCount is null || secondCount is null)
        {
            return;
        }

        if (firstCount == secondCount)
        {
            return;
        }

        AddLint("set-operation-column-count", "error", node, $"{node.BinaryQueryExpressionType} requires both queries to return the same number of columns ({firstCount} and {secondCount}).");
    }

    public override void Visit(CommonTableExpression node)
    {
        ReportRecursiveCte(node);

        var selectCount = SelectElementCount(node.QueryExpression);

        if (node.Columns.Count == 0 || selectCount is null)
        {
            return;
        }

        if (node.Columns.Count == selectCount)
        {
            return;
        }

        AddLint(
            "cte-column-count",
            "error",
            node,
            $"CTE '{node.ExpressionName.Value}' declares {node.Columns.Count} column(s) but its query returns {selectCount}."
        );
    }

    private void ReportRecursiveCte(CommonTableExpression node)
    {
        var recursiveMember = RecursiveMember(node.QueryExpression, node.ExpressionName.Value);
        if (recursiveMember is null || HasVisibleRecursionGuard(recursiveMember))
        {
            return;
        }

        AddLint(
            "recursive-cte",
            "info",
            node,
            $"Recursive CTE '{node.ExpressionName.Value}' has no visible termination condition; verify that recursion makes progress and terminates."
        );
    }

    private static QueryExpression? RecursiveMember(QueryExpression expression, string cteName)
    {
        if (expression is QueryParenthesisExpression parenthesized)
        {
            return RecursiveMember(parenthesized.QueryExpression, cteName);
        }

        if (expression is not BinaryQueryExpression binary
            || binary.BinaryQueryExpressionType != BinaryQueryExpressionType.Union
            || !binary.All)
        {
            return null;
        }

        return TableReferenceFinder.Find(binary.SecondQueryExpression)
            .Any(table => string.Equals(
                table.SchemaObject.BaseIdentifier.Value,
                cteName,
                StringComparison.OrdinalIgnoreCase))
            ? binary.SecondQueryExpression
            : null;
    }

    private static bool HasVisibleRecursionGuard(QueryExpression recursiveMember)
    {
        return QuerySpecificationFinder.Find(recursiveMember)
            .Any(query => query.WhereClause is not null || query.TopRowFilter is not null);
    }

    private int? SelectElementCount(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => SelectElementCount(specification),
        BinaryQueryExpression binary when SelectElementCount(binary.FirstQueryExpression) is { } first
            && SelectElementCount(binary.SecondQueryExpression) == first => first,
        _ => null,
    };

    private int? SelectElementCount(QuerySpecification specification)
    {
        var count = 0;
        foreach (var element in specification.SelectElements)
        {
            if (element is SelectScalarExpression)
            {
                count++;
                continue;
            }

            if (element is not SelectStarExpression star)
            {
                return null;
            }

            if (SelectStarColumnCount(specification.FromClause, star.Qualifier) is not { } starCount)
            {
                return null;
            }

            count += starCount;
        }

        return count;
    }

    private int? SelectStarColumnCount(FromClause? fromClause, MultiPartIdentifier? qualifier)
    {
        var tables = DirectTableAliasFinder.FindScope(fromClause).Tables;

        if (qualifier is null)
        {
            return SelectUnqualifiedStarColumnCount(tables);
        }

        var matchingTables = tables.Where(table => MatchesTableQualifier(table, qualifier)).ToArray();

        if (matchingTables.Length != 1)
        {
            return null;
        }

        return _temporaryTableColumnCounts.TryGetValue(matchingTables[0].SchemaObject.BaseIdentifier.Value, out var count)
            ? count
            : null;
    }

    private int? SelectUnqualifiedStarColumnCount(IReadOnlyList<NamedTableReference> tables)
    {
        if (!HasKnownTemporaryTableColumns(tables))
        {
            return null;
        }

        return tables.Sum(table => _temporaryTableColumnCounts[table.SchemaObject.BaseIdentifier.Value]);
    }

    private bool HasKnownTemporaryTableColumns(IReadOnlyList<NamedTableReference> tables)
    {
        if (tables.Count == 0)
        {
            return false;
        }

        return tables.All(table => _temporaryTableColumnCounts.ContainsKey(table.SchemaObject.BaseIdentifier.Value));
    }

    private static bool MatchesTableQualifier(NamedTableReference table, MultiPartIdentifier qualifier)
    {
        var name = qualifier.Identifiers.LastOrDefault()?.Value;

        if (name is null)
        {
            return false;
        }

        if (string.Equals(table.Alias?.Value, name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(table.SchemaObject.BaseIdentifier.Value, name, StringComparison.OrdinalIgnoreCase);
    }
}
