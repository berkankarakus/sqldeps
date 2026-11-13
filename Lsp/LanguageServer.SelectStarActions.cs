using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private const int SelectStarMultilineThreshold = 5;
    private const int SelectStarIndentSize = 4;

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

        var replacement = FormatSelectStarReplacement(text, diagnostic.Range, columns);

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            replacement.Range,
            replacement.Text,
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

        var replacement = FormatSelectStarReplacement(text, diagnostic.Range, columns);

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            replacement.Range,
            replacement.Text,
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

    private static (LspRange Range, string Text) FormatSelectStarReplacement(
        string text,
        LspRange starRange,
        IReadOnlyList<string> columns)
    {
        string singleLineText = string.Join(", ", columns);

        if (columns.Count < SelectStarMultilineThreshold)
        {
            return (starRange, singleLineText);
        }

        int starOffset = OffsetAt(text, starRange.Start.Line, starRange.Start.Character);
        int lineStart = text.LastIndexOf('\n', Math.Max(starOffset - 1, 0));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = text.IndexOf('\n', starOffset);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;

        int leadingWhitespaceLength = LeadingWhitespaceLength(text, lineStart, lineEnd);
        string lineIndent = text.Substring(lineStart, leadingWhitespaceLength);
        int replacementStart = starOffset;
        int replacementEnd = starOffset + (starRange.End.Character - starRange.Start.Character);
        int precedingWhitespaceStart = HorizontalWhitespaceStart(text, starOffset, lineStart);
        string prefix = text[lineStart..precedingWhitespaceStart].Trim();
        bool isSelectLine = prefix.Length > 0
            && prefix.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
            && !prefix.EndsWith(".", StringComparison.Ordinal);

        if (isSelectLine)
        {
            replacementStart = precedingWhitespaceStart;
        }

        int trailingWhitespaceEnd = HorizontalWhitespaceEnd(text, replacementEnd, lineEnd);
        string tail = text[trailingWhitespaceEnd..lineEnd];
        bool hasFollowingClause = IsFollowingClause(tail);

        if (hasFollowingClause)
        {
            replacementEnd = trailingWhitespaceEnd;
        }

        string columnIndent = isSelectLine
            ? lineIndent + new string(' ', SelectStarIndentSize)
            : lineIndent;
        string clauseIndent = isSelectLine ? lineIndent : IndentationBeforeLine(text, lineStart);
        string formattedColumns = string.Join(",\n" + columnIndent, columns);
        string replacementText = isSelectLine
            ? "\n" + columnIndent + formattedColumns
            : formattedColumns;

        if (hasFollowingClause)
        {
            replacementText += "\n" + clauseIndent;
        }

        return (
            new LspRange(PositionAt(text, replacementStart).Start, PositionAt(text, replacementEnd).Start),
            replacementText);
    }

    private static int LeadingWhitespaceLength(string text, int lineStart, int lineEnd)
    {
        int length = 0;

        while (lineStart + length < lineEnd && text[lineStart + length] is ' ' or '\t')
        {
            length++;
        }

        return length;
    }

    private static int HorizontalWhitespaceStart(string text, int offset, int lineStart)
    {
        int start = offset;

        while (start > lineStart && text[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return start;
    }

    private static int HorizontalWhitespaceEnd(string text, int offset, int lineEnd)
    {
        int end = offset;

        while (end < lineEnd && text[end] is ' ' or '\t')
        {
            end++;
        }

        return end;
    }

    private static bool IsFollowingClause(string tail)
    {
        return Regex.IsMatch(
            tail,
            @"^(?:FROM|WHERE|GROUP|ORDER|HAVING|UNION|EXCEPT|INTERSECT|OPTION)\b",
            RegexOptions.IgnoreCase);
    }

    private static string IndentationBeforeLine(string text, int lineStart)
    {
        int previousLineEnd = lineStart - 1;

        while (previousLineEnd >= 0)
        {
            int previousLineStart = text.LastIndexOf('\n', Math.Max(previousLineEnd - 1, 0));
            previousLineStart = previousLineStart < 0 ? 0 : previousLineStart + 1;
            string previousLine = text[previousLineStart..previousLineEnd].Trim();

            if (previousLine.Length > 0)
            {
                int indentLength = LeadingWhitespaceLength(text, previousLineStart, previousLineEnd);

                return text.Substring(previousLineStart, indentLength);
            }

            previousLineEnd = previousLineStart - 1;
        }

        return "";
    }

    private bool TryFindSingleSourceName(
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

        AnalysisResult analysis = AnalyzeDocument(uri, text);
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

    private bool TryGetSingleSourceName(TableReference source, out string tableName)
    {
        tableName = source switch
        {
            NamedTableReference table when table.SchemaObject.SchemaIdentifier is null => table.SchemaObject.BaseIdentifier.Value,
            NamedTableReference table => ObjectNames.Format(table.SchemaObject, workspaceDefaultSchema, workspaceDefaultDatabase),
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

    private string SelectStarTableName(string rawTableName)
    {
        string tableName = rawTableName.Replace("[", "").Replace("]", "");

        return IsLocalTableName(tableName) ? tableName : Qualify(tableName);
    }

    private string[] SelectStarColumns(string tableName, string uri, string text, int zeroBasedLine)
    {
        AnalysisResult analysis = AnalyzeDocument(uri, text);

        if (!IsLocalTableName(tableName))
        {
            string[] indexedColumns = AllTableColumns()
                .Concat(analysis.TableColumns)
                .Where(column => SameName(column.TableName, tableName))
                .Select(column => column.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (indexedColumns.Length > 0)
            {
                return indexedColumns;
            }
        }

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

}
