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
            .SelectMany(batch => DerivedQueries(batch.Script)
                .Where(candidate => string.Equals(candidate.Alias, alias, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (batch, candidate)))
            .ToArray();

        if (candidates.Length != 1)
        {
            return null;
        }

        var (batch, candidate) = candidates[0];
        var query = candidate.Query;
        if (query.FromClause is null
            || query.SelectElements.Count == 0
            || query.SelectElements.Any(element => element is SelectStarExpression)
            || candidate.ExplicitColumns.Count > 0 && candidate.ExplicitColumns.Count != query.SelectElements.Count
            || !TryUniqueSourceColumn(query.FromClause, column, analysis, out var sourceReference))
        {
            return null;
        }

        var outputColumns = candidate.ExplicitColumns.Count > 0
            ? candidate.ExplicitColumns.Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : OutputColumns(query);
        if (outputColumns.Contains(column))
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

        List<(LspRange Range, string NewText)> edits =
        [
            (
                fromPosition,
                $", {sourceReference} ")
        ];
        if (candidate.ExplicitColumns.Count > 0)
        {
            var lastColumn = candidate.ExplicitColumns[^1];
            edits.Add((
                PositionAt(text, batchStart + lastColumn.StartOffset + lastColumn.FragmentLength),
                $", {column}"));
        }

        return QuickFixEdits(
            $"Add '{column}' to derived query",
            uri,
            edits,
            text,
            diagnostic.Confidence);
    }

    private static IEnumerable<(string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns)> DerivedQueries(TSqlFragment fragment)
    {
        DerivedQueryVisitor visitor = new();
        fragment.Accept(visitor);
        return visitor.Queries;
    }

    private bool TryUniqueSourceColumn(FromClause fromClause, string column, AnalysisResult analysis, out string reference)
    {
        reference = string.Empty;
        DirectTableVisitor visitor = new();
        fromClause.Accept(visitor);
        if (visitor.Tables.Count == 0)
        {
            return false;
        }

        var metadata = AllTableColumns()
            .Concat(analysis.TableColumns)
            .GroupBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var matches = new List<NamedTableReference>();
        foreach (var source in visitor.Tables)
        {
            var sourceName = ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema);
            if (!metadata.TryGetValue(sourceName, out var columns) || columns.Count == 0)
            {
                return false;
            }

            if (columns.Contains(column))
            {
                matches.Add(source);
            }
        }

        if (matches.Count != 1)
        {
            return false;
        }

        var match = matches[0];
        reference = match.Alias is { Value: var alias }
            ? $"{RenderIdentifier(alias)}.{RenderIdentifier(column)}"
            : RenderIdentifier(column);
        return true;
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

    private sealed class DerivedQueryVisitor : TSqlFragmentVisitor
    {
        public List<(string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns)> Queries { get; } = [];

        public override void Visit(QueryDerivedTable node)
        {
            if (node.Alias is not null && TryQuerySpecification(node.QueryExpression, out var query))
            {
                Queries.Add((node.Alias.Value, query, Array.Empty<Identifier>()));
            }

            base.Visit(node);
        }

        public override void Visit(SelectStatement node)
        {
            foreach (var cte in node.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? [])
            {
                if (cte.ExpressionName is not null
                    && TryQuerySpecification(cte.QueryExpression, out var query))
                {
                    Queries.Add((cte.ExpressionName.Value, query, cte.Columns.ToArray()));
                }
            }

            base.Visit(node);
        }
    }

    private static bool TryQuerySpecification(QueryExpression expression, out QuerySpecification query)
    {
        switch (expression)
        {
            case QuerySpecification specification:
                query = specification;
                return true;
            case QueryParenthesisExpression { QueryExpression: var nested }:
                return TryQuerySpecification(nested, out query);
            default:
                query = null!;
                return false;
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
