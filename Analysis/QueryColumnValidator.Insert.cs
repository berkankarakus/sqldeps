using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(InsertStatement node)
        {
            ReportInsertExecuteCount(node);

            if (!TryGetInsertTargetColumns(node, out var tableName, out var knownColumns))
            {
                return;
            }

            ValidateInsertColumns(node, tableName, knownColumns);
            ReportMissingRequiredInsertColumns(node, tableName);
            ReportIdentityInsert(node, tableName);
            ReportComputedInsert(node, tableName);
            ReportDuplicateInsertColumns(node);
            ReportInsertValues(node);
            ReportInsertLiteralTypes(node, tableName);
            ReportInsertSelectCount(node);
            ReportInsertSelectNullability(node, metadataByTable.GetValueOrDefault(tableName) ?? []);
            ReportInsertSelectTypes(node, tableName);
        }

        private bool TryGetInsertTargetColumns(
            InsertStatement node,
            out string tableName,
            out HashSet<string> knownColumns
        )
        {
            tableName = string.Empty;
            knownColumns = null!;

            if (node.InsertSpecification.Target is not NamedTableReference target)
            {
                return false;
            }

            return TryTableColumns(target, out tableName, out knownColumns);
        }
    }
}
