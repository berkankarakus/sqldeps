using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private void ReportInsertSelectNullability(InsertStatement node, IReadOnlyCollection<TableColumn> knownColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0)
            {
                return;
            }

            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            var columnsByName = knownColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            var tables = TablesFor(query, NestedQueries(query));
            foreach (var pair in node.InsertSpecification.Columns.Zip(query.SelectElements))
            {
                if (!TryGetNonNullableTarget(pair.First, pair.Second, columnsByName, out var targetName, out _, out var scalar))
                {
                    continue;
                }

                if (scalar.Expression is NullLiteral nullLiteral)
                {
                    Add("nullability-violation", nullLiteral, $"NULL is assigned to non-nullable column '{targetName}'.", "warning");
                    continue;
                }

                if (IsNullableSourceAssignment(scalar.Expression, tables, query.WhereClause?.SearchCondition, out var sourceMetadata))
                {
                    Add("nullable-source-insert", scalar.Expression, $"Nullable source column '{sourceMetadata.ColumnName}' is inserted into non-nullable column '{targetName}'.", "info");
                }
            }
        }

        private static bool TryGetNonNullableTarget(
            ColumnReferenceExpression target,
            TSqlFragment source,
            IReadOnlyDictionary<string, TableColumn> columnsByName,
            out string targetName,
            out TableColumn targetColumn,
            out SelectScalarExpression scalar)
        {
            targetName = "";
            targetColumn = null!;
            scalar = null!;

            var name = target.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
            if (name is null)
            {
                return false;
            }

            if (!columnsByName.TryGetValue(name, out var column))
            {
                return false;
            }

            if (column.IsNullable != false)
            {
                return false;
            }

            if (source is not SelectScalarExpression scalarExpression)
            {
                return false;
            }

            targetName = name;
            targetColumn = column;
            scalar = scalarExpression;

            return true;
        }

        private bool IsNullableSourceAssignment(
            ScalarExpression expression,
            IReadOnlyList<TableReference> tables,
            BooleanExpression? where,
            out TableColumn sourceMetadata)
        {
            sourceMetadata = null!;

            if (IsDefinitelyNotNull(expression, tables, where))
            {
                return false;
            }

            foreach (var sourceColumn in ColumnReferenceFinder.Find(expression))
            {
                if (!TryGetColumnMetadata(sourceColumn, tables, out var candidate))
                {
                    continue;
                }

                if (candidate.IsNullable != true || IsGuaranteedNotNull(where, sourceColumn))
                {
                    continue;
                }

                sourceMetadata = candidate;

                return true;
            }

            return false;
        }

        private bool IsDefinitelyNotNull(
            ScalarExpression expression,
            IReadOnlyList<TableReference> tables,
            BooleanExpression? where)
        {
            if (expression is NullLiteral or DefaultLiteral)
            {
                return false;
            }

            if (expression is Literal)
            {
                return true;
            }

            if (expression is ParenthesisExpression parenthesis)
            {
                return IsDefinitelyNotNull(parenthesis.Expression, tables, where);
            }

            if (expression is ColumnReferenceExpression column)
            {
                if (!TryGetColumnMetadata(column, tables, out var metadata))
                {
                    return false;
                }

                return metadata.IsNullable == false || IsGuaranteedNotNull(where, column);
            }

            if (expression is CaseExpression caseExpression)
            {
                if (caseExpression.ElseExpression is null
                    || !IsDefinitelyNotNull(caseExpression.ElseExpression, tables, where))
                {
                    return false;
                }

                if (caseExpression is SimpleCaseExpression simpleCase)
                {
                    return simpleCase.WhenClauses.Count > 0
                        && simpleCase.WhenClauses.All(when => IsDefinitelyNotNull(when.ThenExpression, tables, where));
                }

                if (caseExpression is SearchedCaseExpression searchedCase)
                {
                    return searchedCase.WhenClauses.Count > 0
                        && searchedCase.WhenClauses.All(when => IsDefinitelyNotNull(when.ThenExpression, tables, where));
                }

                return false;
            }

            if (expression is CoalesceExpression coalesce)
            {
                return coalesce.Expressions.Any(parameter => IsDefinitelyNotNull(parameter, tables, where));
            }

            if (expression is not FunctionCall function)
            {
                return false;
            }

            if (string.Equals(function.FunctionName.Value, "ISNULL", StringComparison.OrdinalIgnoreCase)
                && function.Parameters.Count == 2)
            {
                return IsDefinitelyNotNull(function.Parameters[1], tables, where);
            }

            if (string.Equals(function.FunctionName.Value, "COALESCE", StringComparison.OrdinalIgnoreCase))
            {
                return function.Parameters.Any(parameter => IsDefinitelyNotNull(parameter, tables, where));
            }

            return false;
        }

        /// <summary>Checks whether a WHERE expression guarantees that a source column is not NULL.</summary>
        private static bool IsGuaranteedNotNull(BooleanExpression? expression, ColumnReferenceExpression column)
        {
            if (expression is null)
            {
                return false;
            }

            if (expression is BooleanIsNullExpression isNull)
            {
                return IsNotNullCheckForColumn(isNull, column);
            }

            if (expression is BooleanParenthesisExpression parenthesis)
            {
                return IsGuaranteedNotNull(parenthesis.Expression, column);
            }

            if (expression is not BooleanBinaryExpression binary)
            {
                return false;
            }

            var first = IsGuaranteedNotNull(binary.FirstExpression, column);
            var second = IsGuaranteedNotNull(binary.SecondExpression, column);

            return CombineNullabilityGuarantees(binary.BinaryExpressionType, first, second);
        }

        private static bool IsNotNullCheckForColumn(
            BooleanIsNullExpression expression,
            ColumnReferenceExpression column)
        {
            if (!expression.IsNot || expression.Expression is not ColumnReferenceExpression reference)
            {
                return false;
            }

            return string.Equals(
                ColumnReferenceText(reference),
                ColumnReferenceText(column),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool CombineNullabilityGuarantees(
            BooleanBinaryExpressionType expressionType,
            bool first,
            bool second)
        {
            if (expressionType == BooleanBinaryExpressionType.And)
            {
                return first || second;
            }

            return first && second;
        }

        private static string ColumnReferenceText(ColumnReferenceExpression column) => string.Join('.',
            column.MultiPartIdentifier?.Identifiers.Select(identifier => identifier.Value) ?? []);
    }
}
