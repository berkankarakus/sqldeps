using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Collects explicit output columns of CTEs and derived tables for local LSP completion.</summary>
internal sealed partial class LocalColumnCollector(int batchStartLine) : TSqlFragmentVisitor
{
    private readonly List<LocalColumn> _columns = [];
    private int _scopeLine;

    public static IReadOnlyList<LocalColumn> Collect(TSqlFragment fragment, int batchStartLine)
    {
        LocalColumnCollector collector = new(batchStartLine);
        fragment.Accept(collector);

        return collector._columns;
    }

    public override void Visit(CommonTableExpression node)
    {
        var names = node.Columns.Count > 0
            ? node.Columns.Select(column => column.Value)
            : OutputColumns(node.QueryExpression);
        Add(node.ExpressionName.Value, names, "CTE", node);
    }

    public override void Visit(ProcedureStatementBody node)
    {
        var previousScope = _scopeLine;
        _scopeLine = batchStartLine + node.StartLine - 1;

        try
        {
            base.Visit(node);
        }
        finally
        {
            _scopeLine = previousScope;
        }
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (node.Alias is not null)
        {
            var columns = node.Columns.Count > 0
                ? node.Columns.Select(column => column.Value)
                : OutputColumns(node.QueryExpression);

            Add(node.Alias.Value, columns, "derived column", node);
        }
    }

    public override void Visit(DeclareTableVariableStatement node)
    {
        Add(node.Body.VariableName.Value, node.Body.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value), "table variable", node);
    }

    public override void Visit(CreateTableStatement node)
    {
        if (!ObjectNames.IsTemporary(node.SchemaObjectName))
        {
            return;
        }

        Add(
            node.SchemaObjectName.BaseIdentifier.Value,
            node.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value),
            "temporary table",
            node);
    }

    public override void Visit(AlterTableAddTableElementStatement node)
    {
        if (!ObjectNames.IsTemporary(node.SchemaObjectName))
        {
            return;
        }

        Add(
            node.SchemaObjectName.BaseIdentifier.Value,
            node.Definition.ColumnDefinitions.Select(column => column.ColumnIdentifier.Value),
            "temporary table",
            node);
    }

    public override void Visit(DropTableStatement node)
    {
        foreach (var table in node.Objects.Where(table => ObjectNames.IsTemporary(table)))
        {
            _columns.Add(new LocalColumn(
                table.BaseIdentifier.Value,
                string.Empty,
                "temporary table drop",
                batchStartLine + node.StartLine - 1,
                IsDrop: true));
        }
    }

    public override void Visit(VariableTableReference node)
    {
        if (node.Alias is null)
        {
            return;
        }

        var columns = _columns
            .Where(column => string.Equals(column.Alias, node.Variable.Name, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.Name)
            .ToArray();

        Add(node.Alias.Value, columns, "table variable", node);
    }

    private void Add(string alias, IEnumerable<string> names, string kind, TSqlFragment node)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _columns.Add(new LocalColumn(alias, name, kind, batchStartLine + node.StartLine - 1, ScopeLine: _scopeLine));
        }
    }

}

internal sealed record LocalColumn(
    string Alias,
    string Name,
    string Kind,
    int Line,
    bool IsDrop = false,
    int ScopeLine = 0);
