namespace SqlDeps.Core;

public sealed record SqlTestSkeleton(
    string ObjectName,
    IReadOnlyList<string> Parameters,
    string Sql
);

public static class SqlTestSkeletonBuilder
{
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
