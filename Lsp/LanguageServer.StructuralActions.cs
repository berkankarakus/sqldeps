using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? MultiTableSelectStarAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule != "select-star" || TextInRange(text, diagnostic.Range) != "*")
        {
            return null;
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
            return null;
        }

        var (batch, query, star) = candidates[0];
        if (star.Qualifier is not null || query.FromClause is null)
        {
            return null;
        }

        var sources = DirectSources(query.FromClause);
        if (sources.Count < 2 || sources.Any(source => source.Alias is null))
        {
            return null;
        }

        var tableColumns = AllTableColumns()
            .Concat(analysis.TableColumns)
            .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(column => column.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var tableNames = sources
            .Select(source => ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema))
            .ToArray();

        if (tableNames.Any(name => !tableColumns.ContainsKey(name) || tableColumns[name].Length == 0)
            || !HasAccessMetadata(tableNames, analysis)
            || !HasRelationshipMetadata(tableNames, analysis))
        {
            return null;
        }

        var columns = sources
            .SelectMany(source => tableColumns[ObjectNames.Format(source.SchemaObject, ObjectNames.StandardDefaultSchema)]
                .Select(column => (Alias: source.Alias!.Value, Column: column)))
            .ToArray();
        if (columns.GroupBy(item => item.Column, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return null;
        }

        var replacement = string.Join(", ", columns.Select(item => $"{item.Alias}.{RenderIdentifier(item.Column)}"));
        if (replacement.Length == 0 || !TryApplyEdit(text, diagnostic.Range, replacement, out var editedText))
        {
            return null;
        }

        if (!IsSafeGeneratedSql(uri, editedText, analysis.SourceFile))
        {
            return null;
        }

        return QuickFix("Expand multi-table SELECT * with qualified columns", uri, diagnostic.Range, replacement, text, diagnostic.Confidence);
    }

    private bool IsSafeGeneratedSql(string uri, string text, string sourceFile)
    {
        var analysis = AnalyzeDocument(sourceFile, text);
        if (analysis.Problems.Count > 0)
        {
            return false;
        }

        var metadata = new MetadataCatalog(
            AllResultColumns().Concat(analysis.ResultColumns),
            AllTableColumns().Concat(analysis.TableColumns),
            AllTableKeys().Concat(analysis.TableKeys),
            AllForeignKeys().Concat(analysis.ForeignKeys),
            AllIndexColumns().Concat(analysis.IndexColumns));
        var findings = QueryColumnValidator.Validate(
            analysis.SourceFile,
            analysis.ParsedBatches,
            metadata,
            analysis.LocalColumns);

        return findings.All(finding => finding.Rule is not (
            "unknown-column" or "ambiguous-column" or "unbound-identifier" or "table-name-after-alias" or "select-star"));
    }

    private bool HasAccessMetadata(IReadOnlyList<string> tableNames, AnalysisResult analysis)
    {
        var keys = AllTableKeys().Concat(analysis.TableKeys);
        var indexes = AllIndexColumns().Concat(analysis.IndexColumns);

        return tableNames.All(tableName =>
            keys.Any(key => string.Equals(key.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            || indexes.Any(index => string.Equals(index.TableName, tableName, StringComparison.OrdinalIgnoreCase)));
    }

    private bool HasRelationshipMetadata(IReadOnlyList<string> tableNames, AnalysisResult analysis)
    {
        var tableSet = tableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AllForeignKeys()
            .Concat(analysis.ForeignKeys)
            .Any(foreignKey => tableSet.Contains(foreignKey.TableName)
                && tableSet.Contains(foreignKey.ReferencedTableName));
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
