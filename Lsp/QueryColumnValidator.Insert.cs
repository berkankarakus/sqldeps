using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        public override void Visit(InsertStatement node)
        {
            ReportInsertExecuteCount(node);

            var target = node.InsertSpecification.Target as NamedTableReference;

            if (target is null)
            {
                return;
            }

            if (!TryTableColumns(target, out var tableName, out var knownColumns))
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
        }
    }
}
