using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? SelectStarAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "select-star")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var match = SelectStarPattern().Match(text);
        var star = match.Groups["star"];

        if (!match.Success)
        {
            return SelectStarActionFromParsedQuery(uri, text, diagnostic);
        }

        if (!star.Success)
        {
            return null;
        }

        int starOffset = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);

        if (star.Index != starOffset)
        {
            return null;
        }

        if (HasJoinOrApply(text, match.Index + match.Length))
        {
            return null;
        }

        string tableName = SelectStarTableName(match.Groups["table"].Value);
        string[] columns = SelectStarColumns(tableName, uri, text, diagnostic.Range.Start.Line);

        if (columns.Length == 0)
        {
            return SelectStarActionFromParsedQuery(uri, text, diagnostic);
        }

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            diagnostic.Range,
            string.Join(", ", columns),
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected star must resolve to one table with known column metadata.");
    }

    private object? SelectStarActionFromParsedQuery(string uri, string text, LintDiagnostic diagnostic)
    {
        if (!TryFindSingleSourceName(uri, text, diagnostic, out var tableName))
        {
            return null;
        }

        string[] columns = SelectStarColumns(tableName, uri, text, diagnostic.Range.Start.Line);

        if (columns.Length == 0)
        {
            return null;
        }

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            diagnostic.Range,
            string.Join(", ", columns),
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected star must resolve to one table with known column metadata.");
    }

    private object? SelectStarQuickFix(
        string title,
        string uri,
        LspRange range,
        string replacement,
        string text,
        string confidence,
        IReadOnlyList<DiagnosticReference> references,
        bool experimental,
        string diagnosticRule,
        string applicability)
    {
        return QuickFix(
            title,
            uri,
            range,
            replacement,
            text,
            confidence,
            references,
            experimental,
            diagnosticRule,
            applicability);
    }

    private static bool TryFindSingleSourceName(
        string uri,
        string text,
        LintDiagnostic diagnostic,
        out string tableName)
    {
        tableName = "";

        if (diagnostic.Rule != "select-star" || TextInRange(text, diagnostic.Range) != "*")
        {
            return false;
        }

        AnalysisResult analysis = DocumentAnalysis.Analyze(uri, text, ObjectNames.StandardDefaultSchema);
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => SelectStarQueries(batch.Script)
                .Select(star => (batch, star.Query, star.Star)))
            .Where(candidate =>
            {
                int starOffset = BatchOffset(text, candidate.batch.StartLine) + candidate.Star.StartOffset;
                LspRange starRange = PositionAt(text, starOffset);

                return starRange.Start.Line == diagnostic.Range.Start.Line
                    && Math.Abs(starRange.Start.Character - diagnostic.Range.Start.Character) <= 2;
            })
            .ToArray();

        if (candidates.Length != 1)
        {
            return false;
        }

        var sources = candidates[0].Query.FromClause?.TableReferences;

        if (sources is null || sources.Count != 1)
        {
            return false;
        }

        return TryGetSingleSourceName(sources[0], out tableName);
    }

    private static bool TryGetSingleSourceName(TableReference source, out string tableName)
    {
        tableName = source switch
        {
            NamedTableReference table when table.SchemaObject.SchemaIdentifier is null => table.SchemaObject.BaseIdentifier.Value,
            NamedTableReference table => ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema),
            VariableTableReference variable => variable.Variable.Name,
            QueryDerivedTable derived when derived.Alias is not null => derived.Alias.Value,
            _ => "",
        };

        return tableName.Length > 0;
    }

    private static bool HasJoinOrApply(string text, int searchStart)
    {
        int statementEnd = text.IndexOf(';', searchStart);
        int end = statementEnd < 0 ? text.Length : statementEnd;
        string statementTail = text[searchStart..end];

        return Regex.IsMatch(statementTail, @"\b(?:JOIN|APPLY)\b", RegexOptions.IgnoreCase);
    }

    private static string SelectStarTableName(string rawTableName)
    {
        string tableName = rawTableName.Replace("[", "").Replace("]", "");

        return IsLocalTableName(tableName) ? tableName : Qualify(tableName);
    }

    private string[] SelectStarColumns(string tableName, string uri, string text, int zeroBasedLine)
    {
        if (!IsLocalTableName(tableName))
        {
            string[] indexedColumns = IndexedColumnsFor(tableName);

            if (indexedColumns.Length > 0)
            {
                return indexedColumns;
            }
        }

        AnalysisResult analysis = AnalyzeDocument(uri, text);
        int targetLine = zeroBasedLine + 1;
        var localColumns = analysis.LocalColumns
            .Where(column => string.Equals(column.Alias, tableName, StringComparison.OrdinalIgnoreCase))
            .Where(column => column.Line <= targetLine)
            .ToArray();
        int latestDropLine = localColumns
            .Where(column => column.IsDrop)
            .Select(column => column.Line)
            .DefaultIfEmpty(0)
            .Max();
        var scopes = localColumns
            .Where(column => !column.IsDrop && column.Line > latestDropLine)
            .GroupBy(column => column.ScopeLine)
            .ToArray();

        if (scopes.Length != 1)
        {
            return [];
        }

        return scopes[0]
            .Select(column => column.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLocalTableName(string tableName)
    {
        if (tableName.Length == 0)
        {
            return false;
        }

        return tableName[0] == '@' || tableName[0] == '#';
    }

    private string[] IndexedColumnsFor(string tableName)
    {
        return AllTableColumns()
            .Where(column => string.Equals(column.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
