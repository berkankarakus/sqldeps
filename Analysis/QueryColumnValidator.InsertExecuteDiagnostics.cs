using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
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
    }
}
