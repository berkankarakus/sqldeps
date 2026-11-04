using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Lsp;

internal sealed partial class LocalColumnCollector
{
    private IEnumerable<string> ColumnsFromSources(FromClause? fromClause, string? qualifier)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (var table in TableReferenceFinder.Find(fromClause))
        {
            var tableName = table.SchemaObject.BaseIdentifier.Value;
            var alias = table.Alias?.Value;
            if (!MatchesQualifier(qualifier, alias, tableName))
            {
                continue;
            }

            var localColumns = LocalColumnsFor(alias ?? tableName);
            result.UnionWith(localColumns);
        }

        foreach (var table in VariableTableReferenceFinder.Find(fromClause))
        {
            var variableName = table.Variable.Name;
            var alias = table.Alias?.Value;
            if (!MatchesQualifier(qualifier, alias, variableName))
            {
                continue;
            }

            var variableColumns = LocalColumnsFor(alias ?? variableName);
            result.UnionWith(variableColumns);
        }

        foreach (var table in QueryDerivedTableFinder.Find(fromClause))
        {
            var alias = table.Alias?.Value;

            if (!MatchesDerivedTableQualifier(qualifier, alias))
            {
                continue;
            }

            result.UnionWith(OutputColumns(table.QueryExpression));
        }

        return result;
    }

    private static bool MatchesDerivedTableQualifier(string? qualifier, string? alias) => alias is not null
        && (qualifier is null || string.Equals(qualifier, alias, StringComparison.OrdinalIgnoreCase));
}
