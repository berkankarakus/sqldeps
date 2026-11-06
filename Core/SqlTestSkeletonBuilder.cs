namespace SqlDeps.Core;

/// <summary>A generated execution skeleton for an indexed procedure or function.</summary>
public sealed record SqlTestSkeleton(
    string ObjectName,
    IReadOnlyList<string> Parameters,
    string Sql
);

/// <summary>Builds safe, non-writing procedure execution skeletons.</summary>
public static class SqlTestSkeletonBuilder
{
    /// <summary>Builds an invocation with nullable placeholders for input parameters.</summary>
    /// <param name="objectName">The procedure or function name.</param>
    /// <param name="parameters">The indexed parameters in declaration order.</param>
    public static SqlTestSkeleton Build(string objectName, IEnumerable<ObjectParameter> parameters)
    {
        ObjectParameter[] parameterList = parameters.OrderBy(parameter => parameter.Ordinal).ToArray();

        if (parameterList.Any(parameter => parameter.IsOutput))
        {
            throw new ArgumentException($"Cannot generate a test skeleton for {objectName}: OUTPUT parameters require caller variables.", nameof(parameters));
        }

        string[] names = parameterList.Select(parameter => parameter.Name).ToArray();
        string arguments = names.Length == 0
            ? $"EXEC {SqlQueryBuilder.QuoteMultipartIdentifier(objectName)};"
            : $"EXEC {SqlQueryBuilder.QuoteMultipartIdentifier(objectName)}\n"
                + string.Join(",\n", names.Select(name => $"    {name} = NULL"))
                + ";";

        return new SqlTestSkeleton(objectName, names, arguments);
    }
}
