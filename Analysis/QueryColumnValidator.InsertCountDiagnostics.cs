using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlTableReference = Microsoft.SqlServer.TransactSql.ScriptDom.TableReference;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
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
