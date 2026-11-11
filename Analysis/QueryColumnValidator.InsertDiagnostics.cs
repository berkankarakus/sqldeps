using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlTableReference = Microsoft.SqlServer.TransactSql.ScriptDom.TableReference;

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
                if (!TryGetNonNullableTarget(pair.First, pair.Second, columnsByName, out var targetName, out var targetColumn, out var scalar))
                {
                    continue;
                }

                if (scalar.Expression is NullLiteral nullLiteral)
                {
                    Add("nullability-violation", nullLiteral, $"NULL is assigned to non-nullable column '{targetName}'.", "warning");
                    continue;
                }

                if (IsNullableSourceInsert(scalar.Expression, tables, query.WhereClause?.SearchCondition, out var sourceMetadata))
                {
                    Add("nullable-source-insert", scalar.Expression, $"Nullable source column '{sourceMetadata.ColumnName}' is inserted into non-nullable column '{targetName}'.", "info");
                }
            }
        }

        private void ReportInsertSelectTypes(InsertStatement node, string tableName)
        {
            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            if (!metadataByTable.TryGetValue(tableName, out var targetColumns))
            {
                return;
            }

            var targetColumnsByName = targetColumns.ToDictionary(
                column => column.ColumnName,
                StringComparer.OrdinalIgnoreCase);
            var tables = TablesFor(query, NestedQueries(query));
            foreach (var pair in node.InsertSpecification.Columns.Zip(query.SelectElements))
            {
                if (pair.Second is not SelectScalarExpression
                    {
                        Expression: ColumnReferenceExpression sourceColumn
                    })
                {
                    continue;
                }

                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null
                    || !TryGetTypeFamilyMismatch(
                        targetName,
                        sourceColumn,
                        targetColumnsByName,
                        tables,
                        out var targetColumn,
                        out var sourceMetadata))
                {
                    continue;
                }

                Add(
                    "insert-select-type",
                    sourceColumn,
                    $"INSERT SELECT source column '{sourceMetadata.ColumnName}' has type '{sourceMetadata.DataType}', but target column '{targetName}' has type '{targetColumn.DataType}'.",
                    "info");
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

        private bool IsNullableSourceInsert(
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
            ColumnReferenceExpression column
        )
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
            bool second
        )
        {
            if (expressionType == BooleanBinaryExpressionType.And)
            {
                return first || second;
            }

            return first && second;
        }

        private static string ColumnReferenceText(ColumnReferenceExpression column) => string.Join('.',
            column.MultiPartIdentifier?.Identifiers.Select(identifier => identifier.Value) ?? []);

        private void ReportInsertExecuteCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not ExecuteInsertSource { Execute: var execute })
            {
                return;
            }

            if (execute.ExecutableEntity is not ExecutableProcedureReference procedure)
            {
                return;
            }

            if (procedure.ProcedureReference?.ProcedureReference?.Name is not { } name)
            {
                return;
            }

            var objectName = ObjectNames.Format(name, ObjectNames.StandardDefaultSchema);
            if (!resultColumnsByObject.TryGetValue(objectName, out var resultColumns))
            {
                return;
            }

            if (!TryInsertTargetColumnCount(node.InsertSpecification, out var targetColumnCount))
            {
                return;
            }

            if (targetColumnCount != resultColumns.Length)
            {
                Add("insert-exec-count", node, $"INSERT targets {targetColumnCount} column(s), but procedure '{objectName}' returns {resultColumns.Length}.", "warning");
            }

            ReportInsertExecuteTypes(node, resultColumns);
        }

        /// <summary>Reports result-set type-family differences only when both sides are explicit.</summary>
        private void ReportInsertExecuteTypes(InsertStatement node, IReadOnlyList<ResultColumn> resultColumns)
        {
            if (node.InsertSpecification.Columns.Count == 0)
            {
                return;
            }

            if (node.InsertSpecification.Target is not NamedTableReference target)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out _))
            {
                return;
            }

            if (!metadataByTable.TryGetValue(tableName, out var targetColumns))
            {
                return;
            }

            var targetByName = targetColumns.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in node.InsertSpecification.Columns.Zip(resultColumns))
            {
                var targetName = pair.First.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
                if (targetName is null)
                {
                    continue;
                }

                if (!targetByName.TryGetValue(targetName, out var targetColumn))
                {
                    continue;
                }

                if (pair.Second.DataType is not { } resultType)
                {
                    continue;
                }

                if (!TypeFamiliesDiffer(targetColumn.DataType, resultType))
                {
                    continue;
                }

                Add("insert-exec-type", pair.First, $"Procedure result column '{pair.Second.ColumnName}' has type '{resultType}', but target column '{targetName}' has type '{targetColumn.DataType}'.", "info");
            }
        }

        private static bool TypeFamiliesDiffer(string first, string second)
        {
            var firstFamily = TypeFamily(first);
            var secondFamily = TypeFamily(second);

            return firstFamily != "other"
                && secondFamily != "other"
                && !string.Equals(firstFamily, secondFamily, StringComparison.Ordinal);
        }

        private bool TryInsertTargetColumnCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (TryGetIndexedTargetColumnCount(specification.Target, out count))
            {
                return true;
            }

            if (TryGetLocalTargetColumnCount(specification.Target, out count))
            {
                return true;
            }

            if (TryGetVariableTargetColumnCount(specification.Target, out count))
            {
                return true;
            }

            count = 0;

            return false;
        }

        private bool TryGetIndexedTargetColumnCount(
            SqlTableReference target,
            out int count)
        {
            count = 0;

            if (target is not NamedTableReference namedTarget)
            {
                return false;
            }

            if (!TryTableColumns(namedTarget, out _, out var knownColumns))
            {
                return false;
            }

            count = knownColumns.Count;

            return true;
        }

        private bool TryGetLocalTargetColumnCount(
            SqlTableReference target,
            out int count)
        {
            count = 0;

            if (target is not NamedTableReference localTarget)
            {
                return false;
            }

            count = LocalColumnsFor(localTarget.SchemaObject.BaseIdentifier.Value, LineOf(localTarget)).Count;

            return count > 0;
        }

        private bool TryGetVariableTargetColumnCount(
            SqlTableReference target,
            out int count)
        {
            count = 0;

            if (target is not VariableTableReference variableTarget)
            {
                return false;
            }

            count = LocalColumnsFor(variableTarget.Variable.Name, LineOf(variableTarget)).Count;

            return count > 0;
        }

        private void ReportInsertSelectCount(InsertStatement node)
        {
            if (node.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification query })
            {
                return;
            }

            if (!TrySelectColumnCount(query, out var selectedColumnCount))
            {
                return;
            }

            if (!TryInsertTargetCount(node.InsertSpecification, out var targetColumnCount))
            {
                return;
            }

            if (targetColumnCount == selectedColumnCount)
            {
                return;
            }

            var targetDescription = node.InsertSpecification.Columns.Count > 0
                ? $"lists {targetColumnCount} target columns"
                : $"targets {targetColumnCount} columns";

            Add("insert-select-count", query, $"INSERT {targetDescription} but SELECT returns {selectedColumnCount} expressions.");
        }

        private bool TryInsertTargetCount(InsertSpecification specification, out int count)
        {
            if (specification.Columns.Count > 0)
            {
                count = specification.Columns.Count;

                return true;
            }

            if (specification.Target is not NamedTableReference target)
            {
                count = 0;

                return false;
            }

            if (TryTableColumns(target, out _, out var columns))
            {
                count = columns.Count;

                return true;
            }

            count = 0;

            return false;
        }

        private bool TrySelectColumnCount(QuerySpecification query, out int count)
        {
            var tables = TablesFor(query, NestedQueries(query));
            count = 0;

            foreach (var element in query.SelectElements)
            {
                if (element is SelectScalarExpression)
                {
                    count++;
                    continue;
                }

                if (element is not SelectStarExpression star)
                {
                    count = 0;

                    return false;
                }

                var qualifier = star.Qualifier?.Identifiers.LastOrDefault()?.Value;
                var sources = qualifier is null ? tables : tables.Where(table => table.Matches(qualifier));
                var sourceList = sources.ToArray();

                if (sourceList.Length == 0)
                {
                    count = 0;

                    return false;
                }

                count += sourceList.Sum(table => table.Columns.Count);
            }

            return true;
        }
    }
}
