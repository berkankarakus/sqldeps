using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

/// <summary>Compares literal expressions using the same semantics as constant evaluation.</summary>
internal static class ConstantVariableLiteralComparer
{
    internal static bool AreEqual(ScalarExpression first, ScalarExpression second)
    {
        if (first is NullLiteral || second is NullLiteral)
        {
            return first is NullLiteral && second is NullLiteral;
        }

        if (first.GetType() != second.GetType())
        {
            return false;
        }

        return ConstantExpressionEvaluator.TryLiteralValue(first, out var firstValue)
            && ConstantExpressionEvaluator.TryLiteralValue(second, out var secondValue)
            && string.Equals(firstValue, secondValue, StringComparison.Ordinal);
    }
}
