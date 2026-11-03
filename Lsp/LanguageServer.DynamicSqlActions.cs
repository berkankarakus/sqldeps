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

        if (!TryReadDynamicExpression(text, expressionRange, out var expression))
        {
            return false;
        }

        if (!TryParameterizeExpression(expression, text[..expressionStart], out var replacement))
        {
            return false;
        }

        if (!TryGetDynamicCallRanges(
                text,
                expressionRange,
                expressionStart,
                out var prefixRange,
                out var closingRange))
        {
            return false;
        }

        edits =
        [
            (prefixRange, "EXEC sys.sp_executesql "),
            (expressionRange, replacement),
            (closingRange, ""),
        ];

        return true;
    }

    private static bool TryReadDynamicExpression(
        string text,
        LspRange expressionRange,
        out string expression)
    {
        expression = "";

        if (!IsDocumentRange(text, expressionRange)
            || TextInRange(text, expressionRange) is not { } value)
        {
            return false;
        }

        expression = value;

        return true;
    }

    private static bool TryGetDynamicCallRanges(
        string text,
        LspRange expressionRange,
        int expressionStart,
        out LspRange prefixRange,
        out LspRange closingRange)
    {
        prefixRange = null!;
        closingRange = null!;

        var call = DynamicExecPrefixPattern().Match(text[..expressionStart]);

        if (!call.Success)
        {
            return false;
        }

        prefixRange = new LspRange(
            PositionAt(text, call.Index).Start,
            PositionAt(text, expressionStart).Start);
        var expressionEnd = OffsetAt(text, expressionRange.End.Line, expressionRange.End.Character);

        if (expressionEnd >= text.Length || text[expressionEnd] != ')')
        {
            return false;
        }

        closingRange = new LspRange(
            PositionAt(text, expressionEnd).Start,
            PositionAt(text, expressionEnd + 1).Start);

        return true;
    }

    private static bool TryParameterizeExpression(string expression, string declarations, out string replacement)
    {
        replacement = "";

        if (!TryReadDynamicParts(expression, out var parts, out var variables))
        {
            return false;
        }

        if (!TryResolveVariableTypes(declarations, variables, out var types))
        {
            return false;
        }

        var parameterNames = CreateParameterNames(variables);

        if (ContainsGeneratedParameterName(parts))
        {
            return false;
        }

        var sqlText = BuildParameterizedSql(parts, parameterNames);

        if (!IsSafeParameterizedSql(sqlText, parameterNames.Values))
        {
            return false;
        }

        replacement = BuildSpExecuteSqlArguments(sqlText, variables, parameterNames, types);

        return true;
    }

    private static bool TryResolveVariableTypes(
        string declarations,
        IReadOnlyList<string> variables,
        out IReadOnlyDictionary<string, string> types)
    {
        var resolvedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            var type = FindVariableType(declarations, variable);

            if (type is null)
            {
                types = resolvedTypes;

                return false;
            }

            resolvedTypes[variable] = type;
        }

        types = resolvedTypes;

        return true;
    }

    private static IReadOnlyDictionary<string, string> CreateParameterNames(IReadOnlyList<string> variables)
    {
        return variables
            .Select((variable, index) => (Variable: variable, Name: $"@__sqldeps_{index}"))
            .ToDictionary(item => item.Variable, item => item.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsGeneratedParameterName(IEnumerable<DynamicSqlPart> parts)
    {
        return parts.Any(part => part.Literal?.Contains("@__sqldeps_", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string BuildParameterizedSql(
        IEnumerable<DynamicSqlPart> parts,
        IReadOnlyDictionary<string, string> parameterNames)
    {
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

        return sql.ToString();
    }

    private static string BuildSpExecuteSqlArguments(
        string sqlText,
        IReadOnlyList<string> variables,
        IReadOnlyDictionary<string, string> parameterNames,
        IReadOnlyDictionary<string, string> types)
    {
        var parameters = variables
            .Select(variable => $"{parameterNames[variable]} {types[variable]}");
        var assignments = variables
            .Select(variable => $"{parameterNames[variable]} = {variable}");

        return $"N'{EscapeSqlLiteral(sqlText)}', N'{EscapeSqlLiteral(string.Join(", ", parameters))}', "
            + string.Join(", ", assignments);
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
            SkipWhitespace(expression, ref cursor);

            if (expectsPart)
            {
                if (!TryReadDynamicPart(expression, ref cursor, readParts, readVariables))
                {
                    parts = [];
                    variables = [];

                    return false;
                }

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

    private static void SkipWhitespace(string expression, ref int cursor)
    {
        while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
        {
            cursor++;
        }
    }

    private static bool TryReadDynamicPart(
        string expression,
        ref int cursor,
        ICollection<DynamicSqlPart> parts,
        ICollection<string> variables)
    {
        var match = DynamicSqlPartPattern().Match(expression, cursor);

        if (!match.Success || match.Index != cursor)
        {
            return false;
        }

        if (match.Groups["variable"].Success)
        {
            var variable = match.Groups["variable"].Value;
            parts.Add(new(null, variable));
            variables.Add(variable);
        }
        else
        {
            var literal = match.Groups["unicode"].Success
                ? match.Groups["unicode"].Value
                : match.Groups["ansi"].Value;
            parts.Add(new(literal.Replace("''", "'"), null));
        }

        cursor += match.Length;

        return true;
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
