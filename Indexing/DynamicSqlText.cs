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
        if (expression is StringLiteral literal)
        {
            sql = literal.Value;
            firstLiteral = literal;

            return true;
        }

        if (expression is not BinaryExpression binary)
        {
            sql = "";
            firstLiteral = null!;

            return false;
        }

        if (!IsConcatenation(binary))
        {
            sql = "";
            firstLiteral = null!;

            return false;
        }

        if (!TryReadConstant(binary.FirstExpression, out var first, out firstLiteral))
        {
            sql = "";

            return false;
        }

        if (!TryReadConstant(binary.SecondExpression, out var second, out _))
        {
            sql = "";

            return false;
        }

        sql = first + second;

        return true;
    }

    private static bool IsConcatenation(BinaryExpression expression) =>
        expression.BinaryExpressionType is BinaryExpressionType.Add or BinaryExpressionType.Concat;
}
