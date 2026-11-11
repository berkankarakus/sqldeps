using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(CreateIndexStatement node)
    {
        if (node.OnName is null)
        {
            return;
        }

        var tableName = Format(node.OnName);
        var filtered = node.FilterPredicate is not null;
        var filterPredicate = FilterPredicate(node.FilterPredicate);

        AddIndexColumns(
            tableName,
            node.Name.Value,
            node.Columns,
            false,
            node.Unique,
            node.Clustered ?? false,
            filtered,
            filterPredicate,
            node
        );

        AddIndexColumns(
            tableName,
            node.Name.Value,
            node.IncludeColumns,
            true,
            node.Unique,
            node.Clustered ?? false,
            filtered,
            filterPredicate,
            node
        );
    }

    private void AddIndexColumns(
        string tableName,
        string indexName,
        IList<ColumnWithSortOrder> columns,
        bool included,
        bool unique,
        bool clustered,
        bool filtered,
        string? filterPredicate,
        TSqlFragment node)
    {
        for (var ordinal = 0; ordinal < columns.Count; ordinal++)
        {
            var column = columns[ordinal];
            _indexColumns.Add(
                new TableIndexColumn(
                    tableName,
                    indexName,
                    column.Column.MultiPartIdentifier.Identifiers[^1].Value,
                    ordinal,
                    included,
                    column.SortOrder == SortOrder.Descending,
                    unique,
                    clustered,
                    filtered,
                    filterPredicate,
                    sourceFile,
                    LineOf(node)
                )
            );
        }
    }

    private void AddIndexColumns(
        string tableName,
        string indexName,
        IList<ColumnReferenceExpression> columns,
        bool included,
        bool unique,
        bool clustered,
        bool filtered,
        string? filterPredicate,
        TSqlFragment node)
    {
        for (var ordinal = 0; ordinal < columns.Count; ordinal++)
        {
            var column = columns[ordinal];
            _indexColumns.Add(
                new TableIndexColumn(
                    tableName,
                    indexName,
                    column.MultiPartIdentifier.Identifiers[^1].Value,
                    ordinal,
                    included,
                    false,
                    unique,
                    clustered,
                    filtered,
                    filterPredicate,
                    sourceFile,
                    LineOf(node)
                )
            );
        }
    }

    private string? FilterPredicate(BooleanExpression? predicate)
    {
        if (predicate is null)
        {
            return null;
        }

        return GenerateScriptText(predicate);
    }
}
