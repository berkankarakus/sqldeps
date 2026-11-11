using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    private static bool IsUpdateSafetyExempt(UpdateStatement statement, UpdateSpecification specification)
    {
        if (HasCommonDmlSafetyExemption(
            specification.Target,
            specification.FromClause,
            specification.TopRowFilter))
        {
            return true;
        }

        if (IsFilteredCteUpdate(statement))
        {
            return true;
        }

        if (IsRestrictedDerivedUpdate(statement))
        {
            return true;
        }

        return false;
    }

    private static bool IsDeleteSafetyExempt(DeleteStatement statement, DeleteSpecification specification)
    {
        if (HasCommonDmlSafetyExemption(
            specification.Target,
            specification.FromClause,
            specification.TopRowFilter))
        {
            return true;
        }

        if (IsTopLimitedCteDelete(statement))
        {
            return true;
        }

        return false;
    }

    private static bool HasCommonDmlSafetyExemption(
        TableReference? target,
        FromClause? fromClause,
        TopRowFilter? topRowFilter)
    {
        if (topRowFilter is not null)
        {
            return true;
        }

        if (IsLocalDmlTarget(target, fromClause))
        {
            return true;
        }

        return HasJoinPredicate(fromClause);
    }

    private static bool IsLocalDmlTarget(TableReference? target, FromClause? fromClause)
    {
        if (target is VariableTableReference)
        {
            return true;
        }

        if (target is not NamedTableReference named)
        {
            return false;
        }

        if (ObjectNames.IsTemporary(named.SchemaObject))
        {
            return true;
        }

        if (fromClause is null)
        {
            return false;
        }

        if (ObjectNames.IsTemporary(ResolveAlias(named, fromClause).SchemaObject))
        {
            return true;
        }

        return VariableTableReferenceFinder.Find(fromClause).Any(reference =>
            string.Equals(reference.Alias?.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTopLimitedCteDelete(DeleteStatement statement)
    {
        var cte = FindTargetCte(statement.DeleteSpecification.Target, statement.WithCtesAndXmlNamespaces);

        if (cte is null)
        {
            return false;
        }

        return HasDirectTopRowFilter(cte.QueryExpression);
    }

    private static bool IsFilteredCteUpdate(UpdateStatement statement)
    {
        var cte = FindTargetCte(statement.UpdateSpecification.Target, statement.WithCtesAndXmlNamespaces);

        if (cte is null)
        {
            return false;
        }

        return HasDirectWhereClause(cte.QueryExpression);
    }

    private static CommonTableExpression? FindTargetCte(
        TableReference? target,
        WithCtesAndXmlNamespaces? withCtes)
    {
        if (target is not NamedTableReference named)
        {
            return null;
        }

        if (named.SchemaObject.SchemaIdentifier is not null)
        {
            return null;
        }

        if (withCtes is null)
        {
            return null;
        }

        return withCtes.CommonTableExpressions.FirstOrDefault(expression =>
            string.Equals(expression.ExpressionName.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRestrictedDerivedUpdate(UpdateStatement statement)
    {
        if (statement.UpdateSpecification.Target is not NamedTableReference target)
        {
            return false;
        }

        var derived = DirectQueryDerivedTableFinder.Find(statement.UpdateSpecification.FromClause)
            .FirstOrDefault(table => string.Equals(table.Alias?.Value, target.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase));

        return derived is not null && HasDirectRestriction(derived.QueryExpression);
    }

    private static bool HasDirectTopRowFilter(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.TopRowFilter is not null,
        QueryParenthesisExpression parenthesized => HasDirectTopRowFilter(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasDirectTopRowFilter(binary.FirstQueryExpression)
            || HasDirectTopRowFilter(binary.SecondQueryExpression),
        _ => false,
    };

    private static bool HasDirectWhereClause(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.WhereClause is not null,
        QueryParenthesisExpression parenthesized => HasDirectWhereClause(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasDirectWhereClause(binary.FirstQueryExpression)
            || HasDirectWhereClause(binary.SecondQueryExpression),
        _ => false,
    };

    private static bool HasDirectRestriction(QueryExpression expression)
    {
        if (HasDirectWhereClause(expression))
        {
            return true;
        }

        return HasDirectTopRowFilter(expression);
    }

    private static bool HasJoinPredicate(FromClause? fromClause) => fromClause?.TableReferences.Any(HasJoinPredicate) == true;

    private static bool HasJoinPredicate(TableReference table) => table switch
    {
        QualifiedJoin join => join.SearchCondition is not null
            || HasJoinPredicate(join.FirstTableReference)
            || HasJoinPredicate(join.SecondTableReference),
        JoinTableReference join => HasJoinPredicate(join.FirstTableReference)
            || HasJoinPredicate(join.SecondTableReference),
        _ => false,
    };

    private static bool Contains(TSqlFragment outer, TSqlFragment inner) =>
        inner.StartOffset >= outer.StartOffset
        && inner.StartOffset + inner.FragmentLength <= outer.StartOffset + outer.FragmentLength;
}
