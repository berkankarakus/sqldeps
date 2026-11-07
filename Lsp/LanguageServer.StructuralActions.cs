using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? MultiTableSelectStarAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (!TryFindMultiTableSelect(text, uri, diagnostic, out var context))
        {
            return null;
        }

        var sources = DirectSources(context.Query.FromClause!);
        if (!TryBuildQualifiedColumns(context, sources, out var replacement))
        {
            return null;
        }

        if (replacement.Length == 0)
        {
            return null;
        }

        if (!TryValidateRewrite(
                text,
                [(diagnostic.Range, replacement)],
                diagnostic.Range.Start.Line,
                out var editedText))
        {
            return null;
        }

        if (!IsSafeGeneratedSql(editedText, context.Analysis, diagnostic.Range.Start.Line))
        {
            return null;
        }

        return QuickFix(
            "Expand multi-table SELECT * with qualified columns",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "Every source must have a unique alias, key/index metadata, and a parse-valid relationship.");
    }

    private static bool TryFindMultiTableSelect(
        string text,
        string uri,
        LintDiagnostic diagnostic,
        out MultiTableSelectContext context)
    {
        context = null!;

        if (diagnostic.Rule != "select-star")
        {
            return false;
        }

        if (TextInRange(text, diagnostic.Range) != "*")
        {
            return false;
        }

        var analysis = AnalyzeDocument(uri, text);
        var targetOffset = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => SelectStarQueries(batch.Script)
                .Select(candidate => (batch, candidate.Query, candidate.Star)))
            .Where(candidate =>
            {
                var starOffset = BatchOffset(text, candidate.batch.StartLine) + candidate.Star.StartOffset;
                return PositionAt(text, starOffset).Start.Line == diagnostic.Range.Start.Line
                    && Math.Abs(starOffset - targetOffset) <= 2;
            })
            .ToArray();

        if (candidates.Length != 1)
        {
            return false;
        }

        var (batch, query, star) = candidates[0];

        if (star.Qualifier is not null)
        {
            return false;
        }

        if (query.FromClause is null)
        {
            return false;
        }

        context = new(analysis, query);

        return true;
    }

    private bool TryBuildQualifiedColumns(
        MultiTableSelectContext context,
        IReadOnlyList<NamedTableReference> sources,
        out string replacement)
    {
        replacement = "";

        if (!HasQualifiedSources(sources))
        {
            return false;
        }

        var tableColumns = BuildTableColumns(context.Analysis);
        var tableNames = sources
            .Select(source => ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema))
            .ToArray();

        if (!HasRequiredMetadata(tableNames, tableColumns, context.Analysis))
        {
            return false;
        }

        var columns = BuildQualifiedColumns(sources, tableColumns);

        if (HasDuplicateColumns(columns))
        {
            return false;
        }

        replacement = string.Join(", ", columns.Select(item => $"{item.Alias}.{RenderIdentifier(item.Column)}"));

        return replacement.Length > 0;
    }

    private static bool HasQualifiedSources(IReadOnlyList<NamedTableReference> sources)
    {
        return sources.Count >= 2 && sources.All(source => source.Alias is not null);
    }

    private Dictionary<string, string[]> BuildTableColumns(AnalysisResult analysis)
    {
        return AllTableColumns()
            .Concat(analysis.TableColumns)
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private bool HasRequiredMetadata(
        IReadOnlyList<string> tableNames,
        IReadOnlyDictionary<string, string[]> tableColumns,
        AnalysisResult analysis)
    {
        if (!HasTableColumns(tableNames, tableColumns))
        {
            return false;
        }

        if (!HasAccessMetadata(tableNames, analysis))
        {
            return false;
        }

        return HasRelationshipMetadata(tableNames, analysis);
    }

    private static bool HasTableColumns(
        IReadOnlyList<string> tableNames,
        IReadOnlyDictionary<string, string[]> tableColumns)
    {
        return tableNames.All(name => tableColumns.ContainsKey(name) && tableColumns[name].Length > 0);
    }

    private static (string Alias, string Column)[] BuildQualifiedColumns(
        IReadOnlyList<NamedTableReference> sources,
        IReadOnlyDictionary<string, string[]> tableColumns)
    {
        return sources
            .SelectMany(source => tableColumns[ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema)]
                .Select(column => (Alias: source.Alias!.Value, Column: column)))
            .ToArray();
    }

    private static bool HasDuplicateColumns(IEnumerable<(string Alias, string Column)> columns)
    {
        return columns
            .GroupBy(item => item.Column, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
    }

    private bool IsSafeGeneratedSql(string text, AnalysisResult originalAnalysis, int changedLine)
    {
        var analysis = DocumentAnalysis.AnalyzeBatchAtLine(originalAnalysis.SourceFile, text, changedLine);
        if (analysis.Problems.Count > 0)
        {
            return false;
        }

        var metadata = new MetadataCatalog(
            AllResultColumns().Concat(originalAnalysis.ResultColumns).Concat(analysis.ResultColumns),
            AllTableColumns().Concat(originalAnalysis.TableColumns).Concat(analysis.TableColumns),
            AllTableKeys().Concat(originalAnalysis.TableKeys).Concat(analysis.TableKeys),
            AllForeignKeys().Concat(originalAnalysis.ForeignKeys).Concat(analysis.ForeignKeys),
            AllIndexColumns().Concat(originalAnalysis.IndexColumns).Concat(analysis.IndexColumns));
        var findings = QueryColumnValidator.Validate(
            analysis.SourceFile,
            analysis.ParsedBatches,
            metadata,
            originalAnalysis.LocalColumns.Concat(analysis.LocalColumns));

        return HasSafeGeneratedFindings(findings);
    }

    private static bool HasSafeGeneratedFindings(IEnumerable<LintFinding> findings)
    {
        return findings.All(finding => finding.Rule is not (
            "unknown-column" or "ambiguous-column" or "unbound-identifier" or "table-name-after-alias" or "select-star"));
    }

    private bool HasAccessMetadata(IReadOnlyList<string> tableNames, AnalysisResult analysis)
    {
        var keys = AllTableKeys().Concat(analysis.TableKeys);
        var indexes = AllIndexColumns().Concat(analysis.IndexColumns);

        return tableNames.All(tableName => HasTableAccessMetadata(tableName, keys, indexes));
    }

    private static bool HasTableAccessMetadata(
        string tableName,
        IEnumerable<TableKey> keys,
        IEnumerable<TableIndexColumn> indexes)
    {
        if (keys.Any(key => string.Equals(key.TableName, tableName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return indexes.Any(index => string.Equals(index.TableName, tableName, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasRelationshipMetadata(IReadOnlyList<string> tableNames, AnalysisResult analysis)
    {
        var tableSet = tableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AllForeignKeys()
            .Concat(analysis.ForeignKeys)
            .Any(foreignKey => ConnectsTables(foreignKey, tableSet));
    }

    private static bool ConnectsTables(TableForeignKey foreignKey, IReadOnlySet<string> tableNames)
    {
        return tableNames.Contains(foreignKey.TableName)
            && tableNames.Contains(foreignKey.ReferencedTableName);
    }

    private static IReadOnlyList<NamedTableReference> DirectSources(FromClause fromClause)
    {
        DirectSourceVisitor visitor = new();
        fromClause.Accept(visitor);
        return visitor.Tables;
    }

    private static IEnumerable<(QuerySpecification Query, SelectStarExpression Star)> SelectStarQueries(TSqlFragment fragment)
    {
        SelectStarVisitor visitor = new();
        fragment.Accept(visitor);
        return visitor.Stars;
    }

    private static string RenderIdentifier(string value)
    {
        return value.All(character => char.IsLetterOrDigit(character) || character == '_')
            ? value
            : $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private sealed record MultiTableSelectContext(
        AnalysisResult Analysis,
        QuerySpecification Query);

    private sealed class DirectSourceVisitor : TSqlFragmentVisitor
    {
        public List<NamedTableReference> Tables { get; } = [];

        public override void Visit(NamedTableReference node) => Tables.Add(node);

        public override void Visit(QueryDerivedTable node)
        {
            // A derived table has a separate scope and is not a direct join source.
        }
    }

    private sealed class SelectStarVisitor : TSqlFragmentVisitor
    {
        public List<(QuerySpecification Query, SelectStarExpression Star)> Stars { get; } = [];

        public override void Visit(QuerySpecification node)
        {
            foreach (var star in node.SelectElements.OfType<SelectStarExpression>())
            {
                Stars.Add((node, star));
            }

            base.Visit(node);
        }
    }
}
