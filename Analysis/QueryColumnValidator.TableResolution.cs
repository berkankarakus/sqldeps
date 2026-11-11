using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
    private sealed partial class ColumnVisitor
    {
        private bool TryTableColumns(NamedTableReference target, out string tableName, out HashSet<string> columns)
        {
            tableName = ObjectNames.Format(target.SchemaObject, ObjectNames.StandardDefaultSchema);
            return columnsByTable.TryGetValue(tableName, out columns!);
        }

        private TableReference? TryTableReference(NamedTableReference reference)
        {
            var tableName = ObjectNames.Format(reference.SchemaObject, ObjectNames.StandardDefaultSchema);

            ReportAmbiguousTableName(reference, tableName);

            if (columnsByTable.TryGetValue(tableName, out var columns))
            {
                return new TableReference(reference.Alias?.Value, tableName, columns, reference);
            }

            var localReference = TryLocalTableReference(reference);
            if (localReference is not null)
            {
                return localReference;
            }

            return TryCteTableReference(reference);
        }

        private TableReference? TryLocalTableReference(NamedTableReference reference)
        {
            var localName = reference.SchemaObject.BaseIdentifier.Value;
            var localColumns = LocalColumnsFor(localName, LineOf(reference));
            if (localColumns.Count == 0)
            {
                return null;
            }

            return new TableReference(reference.Alias?.Value, localName, localColumns, reference);
        }

        private void ReportAmbiguousTableName(NamedTableReference reference, string tableName)
        {
            if (reference.SchemaObject.SchemaIdentifier is not null)
            {
                return;
            }

            var baseName = reference.SchemaObject.BaseIdentifier.Value;
            if (!tablesByBaseName.TryGetValue(baseName, out var candidates))
            {
                return;
            }

            if (candidates.Length < 2)
            {
                return;
            }

            Add("ambiguous-table", reference, $"Table '{baseName}' exists under multiple schemas ({string.Join(", ", candidates)}); qualify it as '{tableName}'.");
        }
    }
}
