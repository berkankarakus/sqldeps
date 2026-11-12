namespace SqlDeps.Analysis;

/// <summary>Exposes safe dynamic SQL parameterization without leaking the implementation type.</summary>
public static class DynamicSqlParameterizationAnalyzer
{
    /// <summary>Builds a parameterized replacement when the expression is safe to rewrite.</summary>
    public static bool TryParameterize(
        string expression,
        string declarations,
        out string replacement)
    {
        return DynamicSqlParameterizer.TryParameterize(expression, declarations, out replacement);
    }
}
