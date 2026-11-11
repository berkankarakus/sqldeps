using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>Recognizes dynamic SQL expressions that can be understood without executing them.</summary>
internal static class DynamicSqlText
{
    public static bool HasConcatenation(ScalarExpression? expression) => expression is BinaryExpression
    {
        BinaryExpressionType: BinaryExpressionType.Add or BinaryExpressionType.Concat
    };

    public static bool HasConcatenation(IEnumerable<ValueExpression>? expressions)
    {
        if (expressions is null)
        {
            return false;
        }

        var values = expressions.ToArray();
        var hasConcatenatedExpression = values.Any(expression => HasConcatenation(expression as ScalarExpression));
        var mixesLiteralsAndValues = values.Any(expression => expression is StringLiteral)
            && values.Any(expression => expression is not StringLiteral);

        return hasConcatenatedExpression || mixesLiteralsAndValues;
    }

    public static bool TryReadConstant(
        IEnumerable<ValueExpression> expressions,
        out string sql,
        out StringLiteral firstLiteral
    )
    {
        var values = expressions.ToList();
        var literals = values.OfType<StringLiteral>().ToList();

        if (literals.Count == 0 || literals.Count != values.Count)
        {
            sql = "";
            firstLiteral = null!;

            return false;
        }

        sql = string.Concat(literals.Select(literal => literal.Value));
        firstLiteral = literals[0];

        return true;
    }

    public static bool TryReadConstant(
        ScalarExpression? expression,
        out string sql,
        out StringLiteral firstLiteral
    )
    {
        sql = "";
        firstLiteral = null!;

        if (expression is StringLiteral literal)
        {
            sql = literal.Value;
            firstLiteral = literal;

            return true;
        }

        if (!TryGetConcatenation(expression, out var binary))
        {
            return false;
        }

        return TryReadConcatenatedConstant(binary, out sql, out firstLiteral);
    }

    private static bool TryGetConcatenation(
        ScalarExpression? expression,
        out BinaryExpression concatenation)
    {
        concatenation = null!;

        if (expression is not BinaryExpression binary)
        {
            return false;
        }

        if (!IsConcatenation(binary))
        {
            return false;
        }

        concatenation = binary;

        return true;
    }

    private static bool TryReadConcatenatedConstant(
        BinaryExpression expression,
        out string sql,
        out StringLiteral firstLiteral)
    {
        sql = "";
        firstLiteral = null!;

        if (!TryReadConstant(expression.FirstExpression, out var first, out firstLiteral))
        {
            return false;
        }

        if (!TryReadConstant(expression.SecondExpression, out var second, out _))
        {
            return false;
        }

        sql = first + second;

        return true;
    }

    private static bool IsConcatenation(BinaryExpression expression) =>
        expression.BinaryExpressionType is BinaryExpressionType.Add or BinaryExpressionType.Concat;
}
