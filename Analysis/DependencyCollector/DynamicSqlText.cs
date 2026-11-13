using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

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

    public static IReadOnlyList<string> LiteralFragments(ScalarExpression? expression)
    {
        List<string> fragments = [];
        AppendLiteralFragments(expression, fragments);

        return fragments;
    }

    public static IReadOnlyList<string> LiteralFragments(IEnumerable<ValueExpression>? expressions)
    {
        List<string> fragments = [];

        if (expressions is null)
        {
            return fragments;
        }

        foreach (var expression in expressions)
        {
            AppendLiteralFragments(expression as ScalarExpression, fragments);
        }

        return fragments;
    }

    public static string? UnresolvedVariableNames(ScalarExpression? expression)
    {
        if (expression is null)
        {
            return null;
        }

        VariableReferenceCollector collector = new();
        expression.Accept(collector);

        return collector.Names.Count == 0
            ? null
            : string.Join(", ", collector.Names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    public static string? UnresolvedVariableNames(IEnumerable<ValueExpression>? expressions)
    {
        if (expressions is null)
        {
            return null;
        }

        VariableReferenceCollector collector = new();
        foreach (var expression in expressions)
        {
            expression.Accept(collector);
        }

        return collector.Names.Count == 0
            ? null
            : string.Join(", ", collector.Names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class VariableReferenceCollector : TSqlFragmentVisitor
    {
        internal HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(VariableReference node)
        {
            Names.Add(node.Name);
        }
    }

    private static void AppendLiteralFragments(ScalarExpression? expression, ICollection<string> fragments)
    {
        if (expression is StringLiteral literal)
        {
            fragments.Add(literal.Value);

            return;
        }

        if (expression is not BinaryExpression binary || !IsConcatenation(binary))
        {
            return;
        }

        AppendLiteralFragments(binary.FirstExpression, fragments);
        AppendLiteralFragments(binary.SecondExpression, fragments);
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
