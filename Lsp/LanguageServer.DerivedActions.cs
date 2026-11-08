using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? DerivedQueryColumnAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (!TryGetDerivedColumnRequest(text, diagnostic, out var alias, out var column))
        {
            return null;
        }

        var analysis = DocumentAnalysis.Analyze(uri, text);
        if (!TryFindDerivedQuery(analysis, alias, out var batch, out var candidate))
        {
            return null;
        }

        if (!TryCreateDerivedColumnEdits(text, batch, candidate, column, analysis, out var edits))
        {
            return null;
        }

        return QuickFixEdits(
            $"Add '{column}' to derived query",
            uri,
            edits,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "Exactly one derived query must expose a unique, parse-valid source column.");
    }

    private static bool TryGetDerivedColumnRequest(
        string text,
        LintDiagnostic diagnostic,
        out string alias,
        out string column)
    {
        alias = "";
        column = "";
        if (diagnostic.Rule != "unknown-column")
        {
            return false;
        }

        var match = UnknownColumnPattern().Match(diagnostic.Message);

        if (!match.Success)
        {
            return false;
        }

        var reference = TextInRange(text, diagnostic.Range);

        if (reference is null)
        {
            return false;
        }

        alias = match.Groups["table"].Value;
        column = match.Groups["column"].Value;

        return reference.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindDerivedQuery(
        AnalysisResult analysis,
        string alias,
        out ParsedSqlBatch batch,
        out (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate)
    {
        var candidates = analysis.ParsedBatches
            .SelectMany(parsedBatch => DerivedQueries(parsedBatch.Script)
                .Where(candidate => string.Equals(candidate.Alias, alias, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (Batch: parsedBatch, Candidate: candidate)))
            .ToArray();

        if (candidates.Length != 1)
        {
            batch = null!;
            candidate = default;

            return false;
        }

        batch = candidates[0].Batch;
        candidate = candidates[0].Candidate;

        return true;
    }

    private bool TryCreateDerivedColumnEdits(
        string text,
        ParsedSqlBatch batch,
        (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate,
        string column,
        AnalysisResult analysis,
        out List<(LspRange Range, string NewText)> edits)
    {
        edits = [];

        if (!TryGetDerivedColumnSource(candidate, column, analysis, out var sourceReference))
        {
            return false;
        }

        if (!TryGetDerivedColumnEditPositions(
                text,
                batch,
                candidate.Query,
                out var batchStart,
                out var fromPosition))
        {
            return false;
        }

        edits = CreateDerivedColumnEdits(
            text,
            batchStart,
            candidate,
            column,
            sourceReference,
            fromPosition);

        return true;
    }

    private bool TryGetDerivedColumnSource(
        (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate,
        string column,
        AnalysisResult analysis,
        out string sourceReference)
    {
        sourceReference = "";
        var query = candidate.Query;

        if (query.FromClause is null)
        {
            return false;
        }

        if (!HasUsableDerivedQuery(candidate, query))
        {
            return false;
        }

        if (!TryUniqueSourceColumn(query.FromClause, column, analysis, out sourceReference))
        {
            return false;
        }

        var outputColumns = candidate.ExplicitColumns.Count > 0
            ? candidate.ExplicitColumns.Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : OutputColumns(query);

        return !outputColumns.Contains(column);
    }

    private static bool HasUsableDerivedQuery(
        (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate,
        QuerySpecification query)
    {
        if (query.SelectElements.Count == 0)
        {
            return false;
        }

        if (query.SelectElements.Any(element => element is SelectStarExpression))
        {
            return false;
        }

        return !HasMismatchedExplicitColumns(candidate, query);
    }

    private static bool HasMismatchedExplicitColumns(
        (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate,
        QuerySpecification query)
    {
        if (candidate.ExplicitColumns.Count == 0)
        {
            return false;
        }

        return candidate.ExplicitColumns.Count != query.SelectElements.Count;
    }

    private static bool TryGetDerivedColumnEditPositions(
        string text,
        ParsedSqlBatch batch,
        QuerySpecification query,
        out int batchStart,
        out LspRange fromPosition)
    {
        batchStart = BatchOffset(text, batch.StartLine);
        fromPosition = PositionAt(text, batchStart + query.FromClause!.StartOffset);
        var selectPosition = PositionAt(text, batchStart + query.SelectElements[0].StartOffset);

        if (fromPosition.Start.Line != selectPosition.Start.Line)
        {
            return false;
        }

        return true;
    }

    private static List<(LspRange Range, string NewText)> CreateDerivedColumnEdits(
        string text,
        int batchStart,
        (string Alias, QuerySpecification Query, IReadOnlyList<Identifier> ExplicitColumns) candidate,
        string column,
        string sourceReference,
        LspRange fromPosition)
    {
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

        return edits;
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

        var metadata = BuildTableColumnMetadata(analysis);

        if (!TryFindUniqueSource(visitor.Tables, column, metadata, out var match))
        {
            return false;
        }

        reference = match.Alias is { Value: var alias }
            ? $"{RenderIdentifier(alias)}.{RenderIdentifier(column)}"
            : RenderIdentifier(column);

        return true;
    }

    private Dictionary<string, HashSet<string>> BuildTableColumnMetadata(AnalysisResult analysis)
    {
        return AllTableColumns()
            .Concat(analysis.TableColumns)
            .GroupBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryFindUniqueSource(
        IReadOnlyList<NamedTableReference> sources,
        string column,
        IReadOnlyDictionary<string, HashSet<string>> metadata,
        out NamedTableReference match)
    {
        match = null!;

        foreach (var source in sources)
        {
            var sourceName = ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema);

            if (!metadata.TryGetValue(sourceName, out var columns) || columns.Count == 0)
            {
                match = null!;

                return false;
            }

            if (!columns.Contains(column))
            {
                continue;
            }

            if (match is not null)
            {
                match = null!;

                return false;
            }

            match = source;
        }

        return match is not null;
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
