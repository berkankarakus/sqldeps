using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;
using SqlTableReference = Microsoft.SqlServer.TransactSql.ScriptDom.TableReference;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
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
