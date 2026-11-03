using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? DerivedQueryColumnAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule != "unknown-column")
        {
            return null;
        }

        var match = UnknownColumnPattern().Match(diagnostic.Message);
        var reference = TextInRange(text, diagnostic.Range);
        if (!match.Success || reference is null)
        {
            return null;
        }

        var alias = match.Groups["table"].Value;
        var column = match.Groups["column"].Value;
        if (!reference.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var analysis = DocumentAnalysis.Analyze(uri, text);
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => DerivedTables(batch.Script)
                .Where(table => string.Equals(table.Alias?.Value, alias, StringComparison.OrdinalIgnoreCase))
                .Select(table => (batch, table)))
            .ToArray();

        if (candidates.Length != 1)
        {
            return null;
        }

        var (batch, derivedTable) = candidates[0];
        if (derivedTable.QueryExpression is not QuerySpecification query
            || query.FromClause is null
            || query.SelectElements.Count == 0
            || !TryDirectSourceTable(query.FromClause, out var sourceTable))
        {
            return null;
        }

        var sourceName = ObjectNames.Format(sourceTable.SchemaObject, ObjectNames.StandardDefaultSchema);
        var sourceColumns = AllTableColumns()
            .Where(item => string.Equals(item.TableName, sourceName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sourceColumns.Contains(column) || OutputColumns(query).Contains(column, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var batchStart = BatchOffset(text, batch.StartLine);
        var fromOffset = batchStart + query.FromClause.StartOffset;
        var fromPosition = PositionAt(text, fromOffset);
        var selectPosition = PositionAt(text, batchStart + query.SelectElements[0].StartOffset);
        if (fromPosition.Start.Line != selectPosition.Start.Line)
        {
            return null;
        }

        return QuickFix(
            $"Add '{column}' to derived query",
            uri,
            fromPosition,
            $", {column} ",
            text,
            diagnostic.Confidence);
    }

    private static IEnumerable<QueryDerivedTable> DerivedTables(TSqlFragment fragment)
    {
        DerivedTableVisitor visitor = new();
        fragment.Accept(visitor);
        return visitor.Tables;
    }

    private static bool TryDirectSourceTable(FromClause fromClause, out NamedTableReference table)
    {
        DirectTableVisitor visitor = new();
        fromClause.Accept(visitor);
        table = visitor.Tables.Count == 1 ? visitor.Tables[0] : null!;
        return table is not null;
    }

    private static IReadOnlySet<string> OutputColumns(QuerySpecification query)
    {
        return query.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(element => element.ColumnName?.Value
                ?? (element.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int BatchOffset(string text, int startLine)
    {
        return OffsetAt(text, Math.Max(0, startLine - 1), 0);
    }

    private sealed class DerivedTableVisitor : TSqlFragmentVisitor
    {
        public List<QueryDerivedTable> Tables { get; } = [];

        public override void Visit(QueryDerivedTable node)
        {
            Tables.Add(node);
            base.Visit(node);
        }
    }

    private sealed class DirectTableVisitor : TSqlFragmentVisitor
    {
        public List<NamedTableReference> Tables { get; } = [];

        public override void Visit(NamedTableReference node) => Tables.Add(node);

        public override void Visit(QueryDerivedTable node)
        {
            // A derived table has its own source scope; do not count its tables as direct sources.
        }
    }
}
