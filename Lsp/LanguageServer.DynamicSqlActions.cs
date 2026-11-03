using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? DynamicSqlParameterAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null || diagnostic.Rule != "dynamic-sql-concatenation")
        {
            return null;
        }

        var expressionStart = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
        if (!TryCreateDynamicSqlEdits(text, diagnostic.Range, expressionStart, out var edits))
        {
            return null;
        }

        return QuickFixEdits(
            "Convert dynamic SQL to sp_executesql parameters",
            uri,
            edits,
            text,
            diagnostic.Confidence);
    }

    private static bool TryCreateDynamicSqlEdits(
        string text,
        LspRange expressionRange,
        int expressionStart,
        out IReadOnlyList<(LspRange Range, string NewText)> edits)
    {
        edits = [];

        if (!IsDocumentRange(text, expressionRange) || TextInRange(text, expressionRange) is not { } expression)
        {
            return false;
        }

        if (!TryParameterizeExpression(expression, text[..expressionStart], out var replacement))
        {
            return false;
        }

        var prefix = text[..expressionStart];
        var call = DynamicExecPrefixPattern().Match(prefix);
        if (!call.Success)
        {
            return false;
        }

        var prefixRange = new LspRange(
            PositionAt(text, call.Index).Start,
            PositionAt(text, expressionStart).Start);
        var expressionEnd = OffsetAt(text, expressionRange.End.Line, expressionRange.End.Character);
        if (expressionEnd >= text.Length || text[expressionEnd] != ')')
        {
            return false;
        }

        var closingRange = new LspRange(
            PositionAt(text, expressionEnd).Start,
            PositionAt(text, expressionEnd + 1).Start);
        edits =
        [
            (prefixRange, "EXEC sys.sp_executesql "),
            (expressionRange, replacement),
            (closingRange, ""),
        ];

        return true;
    }

    private static bool TryParameterizeExpression(string expression, string declarations, out string replacement)
    {
        replacement = "";
        if (!TryReadDynamicParts(expression, out var parts, out var variables))
        {
            return false;
        }

        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
        {
            var type = FindVariableType(declarations, variable);
            if (type is null)
            {
                return false;
            }

            types[variable] = type;
        }

        var parameterNames = variables
            .Select((variable, index) => (Variable: variable, Name: $"@__sqldeps_{index}"))
            .ToDictionary(item => item.Variable, item => item.Name, StringComparer.OrdinalIgnoreCase);
        if (parts.Any(part => part.Literal?.Contains("@__sqldeps_", StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        var sql = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Variable is { } variable)
            {
                sql.Append(parameterNames[variable]);

                continue;
            }

            sql.Append(part.Literal);
        }

        var sqlText = sql.ToString();
        if (!IsSafeParameterizedSql(sqlText, parameterNames.Values))
        {
            return false;
        }

        var parameters = variables
            .Select(variable => $"{parameterNames[variable]} {types[variable]}");
        replacement = $"N'{EscapeSqlLiteral(sqlText)}', N'{EscapeSqlLiteral(string.Join(", ", parameters))}', "
            + string.Join(", ", variables.Select(variable => $"{parameterNames[variable]} = {variable}"));

        return true;
    }

    private static bool TryReadDynamicParts(
        string expression,
        out IReadOnlyList<DynamicSqlPart> parts,
        out IReadOnlyList<string> variables)
    {
        List<DynamicSqlPart> readParts = [];
        List<string> readVariables = [];
        var cursor = 0;
        var expectsPart = true;

        while (cursor < expression.Length)
        {
            while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
            {
                cursor++;
            }

            if (expectsPart)
            {
                var match = DynamicSqlPartPattern().Match(expression, cursor);
                if (!match.Success || match.Index != cursor)
                {
                    parts = [];
                    variables = [];

                    return false;
                }

                if (match.Groups["variable"].Success)
                {
                    var variable = match.Groups["variable"].Value;
                    readParts.Add(new(null, variable));
                    readVariables.Add(variable);
                }
                else
                {
                    var literal = match.Groups["unicode"].Success
                        ? match.Groups["unicode"].Value
                        : match.Groups["ansi"].Value;
                    readParts.Add(new(literal.Replace("''", "'"), null));
                }

                cursor += match.Length;
                expectsPart = false;

                continue;
            }

            if (cursor >= expression.Length || expression[cursor] != '+')
            {
                break;
            }

            cursor++;
            expectsPart = true;
        }

        parts = readParts;
        variables = readVariables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return !expectsPart && parts.Count > 1 && variables.Count > 0;
    }

    private static string? FindVariableType(string declarations, string variable)
    {
        foreach (Match match in DynamicVariableDeclarationPattern().Matches(declarations))
        {
            if (!string.Equals(match.Groups["name"].Value, variable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = match.Groups["type"].Value.Trim();
            return IsSupportedDynamicParameterType(type) ? type : null;
        }

        return null;
    }

    private static bool IsSupportedDynamicParameterType(string type)
    {
        var baseType = type.Split('(')[0].Trim();

        return baseType.ToUpperInvariant() is
            "BIGINT" or "BIT" or "CHAR" or "DATE" or "DATETIME" or "DATETIME2" or
            "DECIMAL" or "INT" or "MONEY" or "NCHAR" or "NTEXT" or "NUMERIC" or
            "NVARCHAR" or "REAL" or "SMALLDATETIME" or "SMALLINT" or "SMALLMONEY" or
            "TEXT" or "TIME" or "TINYINT" or "UNIQUEIDENTIFIER" or "VARBINARY" or "VARCHAR";
    }

    private static bool IsSafeParameterizedSql(string sql, IEnumerable<string> parameterNames)
    {
        if (DynamicIdentifierParameterPattern().IsMatch(sql))
        {
            return false;
        }

        using StringReader reader = new(sql);
        _ = new TSql160Parser(initialQuotedIdentifiers: true).Parse(reader, out IList<ParseError> errors);

        return errors.Count == 0 && parameterNames.Any();
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private sealed record DynamicSqlPart(string? Literal, string? Variable);

    [GeneratedRegex(@"\bEXEC(?:UTE)?\s*\(\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DynamicExecPrefixPattern();

    [GeneratedRegex(@"(?:N'(?<unicode>(?:''|[^'])*)'|'(?<ansi>(?:''|[^'])*)'|(?<variable>@[A-Za-z_][A-Za-z0-9_$]*))")]
    private static partial Regex DynamicSqlPartPattern();

    [GeneratedRegex(@"(?im)(?<name>@[A-Za-z_][A-Za-z0-9_$]*)\s+(?<type>(?:bigint|bit|char|date|datetime2?|decimal|int|money|nchar|ntext|numeric|nvarchar|real|smalldatetime|smallint|smallmoney|text|time|tinyint|uniqueidentifier|varbinary|varchar)(?:\s*\(\s*(?:max|\d+)(?:\s*,\s*\d+)?\s*\))?)(?=\s|$|[,=)])")]
    private static partial Regex DynamicVariableDeclarationPattern();

    [GeneratedRegex(@"\b(?:DELETE\s+FROM|FROM|GROUP\s+BY|INSERT\s+INTO|JOIN|ORDER\s+BY|SELECT|UPDATE)\s+@__sqldeps_\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex DynamicIdentifierParameterPattern();
}
