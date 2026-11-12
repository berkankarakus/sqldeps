using SqlDeps.Analysis;

namespace SqlDeps.Core;

/// <summary>Exposes safe dynamic SQL parameterization to protocol adapters.</summary>
public static class DynamicSqlParameterizationService
{
    /// <summary>Builds a parameterized replacement when the expression is safe to rewrite.</summary>
    public static bool TryParameterize(
        string expression,
        string declarations,
        out string replacement)
    {
        return DynamicSqlParameterizationAnalyzer.TryParameterize(expression, declarations, out replacement);
    }
}
