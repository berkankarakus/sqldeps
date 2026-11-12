using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(InPredicate node)
        {
            ReportNullableNotInSubquery(node);
            base.Visit(node);
        }

        private void ReportNullableNotInSubquery(InPredicate node)
        {
            if (!node.NotDefined
                || node.Subquery is null
                || !TryGetSubqueryColumnMetadata(
                    node.Subquery.QueryExpression,
                    out var sourceColumn,
                    out var sourceReference,
                    out var sourceFilter)
                || sourceColumn.IsNullable != true)
            {
                return;
            }

            if (IsGuaranteedNotNull(sourceFilter, sourceReference))
            {
                return;
            }

            Add(
                "not-in-null",
                node,
                $"NOT IN subquery reads nullable column '{sourceColumn.TableName}.{sourceColumn.ColumnName}'; NULL can make non-matching rows evaluate UNKNOWN. Use NOT EXISTS or filter out NULLs.",
                "warning"
            );
        }

        private bool TryGetSubqueryColumnMetadata(
            QueryExpression queryExpression,
            out TableColumn sourceColumn,
            out ColumnReferenceExpression sourceReference,
            out BooleanExpression? sourceFilter)
        {
            sourceColumn = null!;
            sourceReference = null!;
            sourceFilter = null;
            var query = QuerySpecificationFinder.Find(queryExpression).FirstOrDefault();
            if (query is null
                || query.SelectElements.Count != 1
                || query.SelectElements[0] is not SelectScalarExpression scalar
                || scalar.Expression is not ColumnReferenceExpression column)
            {
                return false;
            }

            sourceReference = column;
            sourceFilter = query.WhereClause?.SearchCondition;

            var tables = DirectTables(query, NestedQueries(query));
            if (tables.Count != 1)
            {
                return false;
            }

            return TryGetColumnMetadata(column, tables, out sourceColumn);
        }
    }
}
